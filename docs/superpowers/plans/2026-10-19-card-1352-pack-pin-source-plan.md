# CARD-1352: one source for the net9 pack pin and the image SDK probe

Plan task: 448d04b9-8853-49b5-919a-0167ea818b14 (Plan, Frontier). Base:
a0f3c4f3e728655933da1782981a88fb2b7b3a56 (master). Card: CARD-1352, filed by the
Final Review of the seed-smoke targeting-pack pin at
96443baf855973a7c8f5d3a776a155c80b20d7db (plan
`docs/superpowers/plans/2026-10-19-seed-smoke-targeting-pack-plan.md`; that commit is
an ancestor of this base). Master-only: no Dockerfile change, no image build, no
rollout, no restart. Verification design is folded into this plan (the brief asks
for the red-first witnesses and the closed checkpoint table), so the next stage is
Code.

Platform: Linux lane. Every checkpoint needs bash, pwsh and the image SDK with the
net9 packs, which the session-testing image carries. Placement follows
`GET /api/runner-defaults` at dispatch time (global default `server2`); do not pin a
host. No session that waits for input is added or changed, so the parking checklist
does not apply.

## Problem

The 2026-10-19 pin made both smoke projects select the image packs explicitly, and
the drift test ties the four `TargetingPackVersion` values to Dockerfile
`ARG NET9_PACK_VERSION`. Four other copies of the same fact stayed literal and are
not read by any test: the pack-directory probes in `c849_smoke` and in the image
`net9-offline` row, `RuntimeFrameworkVersion` in both projects, and the masked-pack
control's mount target. The row also asserts the default SDK is exactly `10.0.401`,
while the standing image has `10.0.402`. A coupled ARG plus `TargetingPackVersion`
bump therefore stays green in the contract and fails closed later, and the image row
is red today on the SDK assertion before it restores anything.

## Ground truth

| Card / brief says | What the code and this runner show | Consequence |
|---|---|---|
| The pack-directory probes in `c849_smoke` and the `net9-offline` row stay literal `9.0.20` and are not read by the drift test. | Confirmed: `scripts/c590-remote.sh:1541` and `docker/session-runner-grok/verify-codex-image.sh:231`. A third image-pack path the card does not list is also literal: the masked-pack control mounts an empty volume at `/usr/share/dotnet/packs/$pack/9.0.20` (`c590-remote.sh:4287`) and its fixture expects that exact string (`scripts/fixtures/c1192-workspace-contract.sh:2489,2495,2535`). | Three path consumers, not two. A pin bump that leaves the mask behind makes the control mount an empty directory nobody probes, so the control reports `ControlNotSensitive` and the Fixture gate fails for the wrong reason. |
| `RuntimeFrameworkVersion` stays literal `9.0.20` in both projects. | Confirmed (`c590-remote.sh:1553`, `verify-codex-image.sh:255`). Measured here, offline, cleared sources, empty packages, SDK 10.0.402: the current shape restores, builds and runs (`runtimeconfig` 9.0.20, packages still empty). Without `RuntimeFrameworkVersion`: `NU1100 Microsoft.NETCore.App.Host.linux-x64 (= 9.0.21)`. With `RuntimeFrameworkVersion` 9.0.21 and `TargetingPackVersion` 9.0.20: the same `NU1100`. Without `TargetingPackVersion`: `NU1100` for both `.Ref` packs at 9.0.21. | `RuntimeFrameworkVersion` is the apphost-pack selector. It must follow the same pin as the targeting packs, so the single source has three XML consumers per project, not two. |
| The `net9-offline` row requires default SDK `10.0.401`; the Fixture diagnosis saw `10.0.402`. | `verify-codex-image.sh:244-245`. This runner is the standing session-testing image: `dotnet --version` prints `10.0.402`, `--list-sdks` lists exactly that one SDK, runtimes are 9.0.21 and 10.0.13, `/usr/share/dotnet/packs/*` hold `10.0.13` and `9.0.20`, and the SDK's `Microsoft.NETCoreSdk.BundledVersions.props` declares net9 `TargetingPackVersion="9.0.21"` and `AppHostPackVersion="9.0.21"`. | The row as written fails on the standing image at the SDK assertion, before any pack is used. The pinned project no longer depends on the SDK's bundled net9 defaults, so an exact SDK patch protects nothing; the row needs the image's SDK policy, not a patch number. |
| CARD-0916 must keep `TargetingPackVersion` if it drops the runtime pins. | Keeping `TargetingPackVersion` is necessary but not sufficient: dropping `RuntimeFrameworkVersion` alone already fails offline (row above). | This plan does not implement CARD-0916. The measurement goes on that card in the handoff. |
| The Dockerfile ARG is the natural single source. | `ARG NET9_PACK_VERSION=9.0.20` lives inside the `net9-packs` stage (`Dockerfile:55`). No build site passes `--build-arg NET9_PACK_VERSION` (the four build sites pass only `SOURCE_REVISION`), so the default is the value. `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` asserts the literal pin-pair inside that stage body and asserts the `build` stage `FROM` contains `sdk:10.0` ("sdk10-policy"). | Keep the ARG where it is; read it as text. Do not move it to global scope and do not pin the SDK tag. |
| The image SDK is "10.0.401, selected by global.json" (`docs/docker-stack.md:519`). | `global.json` is `10.0.204` with `latestMinor`; it governs repo builds, not the image. The image SDK is whatever the floating `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build` resolves to at build time (10.0.402 on the 2026-10-18 canary and here). | The one source for the image SDK is that `FROM` line's tag. The doc sentence is wrong and is corrected in S2. |
| Consumers can read the ARG. | The inner smoke runs as `docker exec ... /bin/sh -s` fed by a quoted heredoc (`c590-remote.sh:1533`): nothing from the host expands inside it. The probe row is bind-mounted alone into a throwaway container by `scripts/verify-card0660-codex-image.ps1:159-176`, by `c849_fixture_warm_probe` (`c590-remote.sh:4855-4868`) and run directly by `scripts/fixtures/c913-image-contract.sh:124`. Every launcher has the checkout: `ensure_checkout` pins `$CHECKOUT` at `$SHA`, and the wrapper proves checkout HEAD equals `-SourceRevision`, a clean tracked tree and the image's `org.opencontainers.image.revision` label equals it. | Transport the value as environment from each launcher; the Dockerfile the launcher reads is the one the image was built from. |
| Fixtures model the consumers. | `scripts/fixtures/c913-image-contract.sh` runs the row with a fake `dotnet` that prints `10.0.401` and fake packs at `9.0.20`, and runs the extracted smoke heredoc the same way. `RemoteScriptContractTests.C1066FixtureHarness` sets `CHECKOUT` to a directory that does not exist and runs `c849_fixture_warm_probe` under `set -u`. `scripts/fixtures/seed-smoke-targeting-packs.ps1` builds the extracted project XML verbatim with the real SDK. | Each fixture needs the value or a Dockerfile to read; the native helper must substitute the token before building. |

## Decisions

- D-1 Source of truth. The Dockerfile keeps declaring both facts where it does today:
  `ARG NET9_PACK_VERSION=<x.y.z>` in the `net9-packs` stage, and the `build` stage
  `FROM mcr.microsoft.com/dotnet/sdk:<tag> AS build` line. Nothing else declares a
  pack version or an SDK expectation. Rejected: a separate version file (every
  build site would have to pass `--build-arg`, and the ARG default would become a
  second copy or go away); an `ENV`/`LABEL` baked into the image (an image older
  than the change would fail on a missing variable instead of the honest
  `ImagePackMissing`, and moving the ARG out of its stage breaks the pin-pair test).
- D-2 Transport. Each launcher reads the two values from the checkout Dockerfile and
  passes them as environment: `NET9_PACK_VERSION` (same name as the ARG) and
  `SDK_IMAGE_TAG`. In `c590-remote.sh` two small helpers do the reading,
  `c849_pack_pin` and `c849_sdk_tag`, each `sed` over
  `$CHECKOUT/docker/session-runner-grok/Dockerfile` with CRs stripped, printing the
  value or returning 1. Rejected: bind-mounting the Dockerfile into probe containers
  (one more bind and the checkout-input-only rule in `RemoteScriptContractTests`
  would need widening); reading `/work/repos/antiphon` from inside the smoke (that
  checkout is not necessarily at the image's SHA).
- D-3 Fail closed at the point of use. The inner smoke validates
  `${NET9_PACK_VERSION}` (`x.y.z` shape) right after its existing `NUGET_*`/`NPM_*`
  tests and before the broker read, printing `ImagePackPinInvalid` and exiting 1, so
  the `smoke-environment-refused` control ordering holds. The row validates the pin
  after its emptiness checks and before the pack loop (`ImagePackPinMissing`), and
  the tag before the SDK query (`SdkTagMissing`). The host side reports
  `ImagePackPinUnreadable` when the Dockerfile does not declare the ARG.
- D-4 SDK probe ties to the `FROM` tag, not to a pinned patch. The row asserts
  `dotnet --version` equals the tag or starts with `<tag>.`, that `dotnet --list-sdks`
  lists exactly one SDK and that it is the same version, and keeps recording the
  actual value (`SDK=` in the ok detail). A floating tag is a tested policy
  (`sdk10-policy`), the explicit pack selection makes the smoke independent of the
  SDK's bundled defaults, and the exactly-one assertion keeps the documented
  no-side-by-side-SDK decision. Rejected: pinning `FROM sdk:10.0.402` through an ARG
  (policy change, image rebuild per patch, breaks `sdk10-policy`); deleting the SDK
  check (loses the band and single-SDK qualification); reading the band from
  `global.json` (it governs repo builds, accepts any 10.0.x at or above 10.0.204, and
  would be a third reader).
- D-5 `RuntimeFrameworkVersion` stays and follows the pin (measured: it selects the
  apphost pack). CARD-0916's request to drop the runtime pins is incompatible with
  the offline contract on SDK 10.0.402 unless the apphost pack is selected another
  way; this plan records the measurement for that card and does not act on it.
- D-6 Reviewed literals stay. `Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing`
  keeps asserting `ARG NET9_PACK_VERSION=9.0.20` next to `NET9_SDK_VERSION=9.0.318`:
  that is the pin-pair review gate (a bump must touch the test, as the Codex, Grok
  and jq pins do). The fixture `archive` mode keeps its own `9.0.20`, which is the
  exported value its fake archive is built from. The legacy NuGet-cache payload
  paths (`microsoft.netcore.app.host.linux-x64/9.0.20` in the c849 recovery/refill
  code, `c913-seed-contract.sh`, `c973-marker-reader.sh`) name a cached package
  under the legacy marker schema with its own `payload-sha256`; they are a different
  artifact and out of scope.
- D-7 The drift test method keeps its name,
  `Net9_smoke_reference_selects_the_image_targeting_pack`, because the pending
  mutation ledger for the previous card names it. Its assertion changes from "equals
  the ARG value" to "equals the token `${NET9_PACK_VERSION}`", and it now also checks
  `RuntimeFrameworkVersion`. That ledger's PC-2 ("change the ARG only") becomes void
  by construction; its replacement is PC-1 below.
- D-8 Witness values. Fixtures export `NET9_PACK_VERSION=9.0.77` and
  `SDK_IMAGE_TAG=10.0` with a fake SDK that prints `10.0.402`, so any consumer still
  carrying `9.0.20` or `10.0.401` fails. The native rows run the real standing SDK
  and packs and are red at S1 on the SDK literal alone.
- D-9 No image rebuild and no rollout here. The live qualification after land is the
  caller's `verify-card0660-codex-image.ps1 -Target session-testing` run and the
  Fixture/Inventory acceptance already named on the card.

## Design

Dockerfile (unchanged): `ARG NET9_PACK_VERSION=9.0.20`, `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`.

Readers and consumers:

| Consumer | Today | After |
|---|---|---|
| `c849_pack_pin`, `c849_sdk_tag` (new, `scripts/c590-remote.sh`) | none | `sed -n 's/^ARG NET9_PACK_VERSION=\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\)$/\1/p'` and `sed -n 's|^FROM mcr\.microsoft\.com/dotnet/sdk:\([0-9][0-9A-Za-z.-]*\) AS build$|\1|p'` over the checkout Dockerfile after `tr -d '\r'`; first match; empty means return 1. No inner `}` lines, so `RemoteScriptContractTests.Block` extracts them. |
| `c849_smoke` outer (`c590-remote.sh:1531`) | `docker exec -u 1654:1654 -e HOME=/home/app -i` | resolves `pin="$(c849_pack_pin)"` or `write_result false ImagePackPinUnreadable 2`; adds `-e NET9_PACK_VERSION="$pin"`. |
| `c849_smoke` inner heredoc | `path=".../$pack/9.0.20"`; project with three `9.0.20` literals | validates `${NET9_PACK_VERSION}` after the four env tests; `path="/usr/share/dotnet/packs/$pack/${NET9_PACK_VERSION}"`; the project heredoc becomes unquoted (`<<EOF`) with `${NET9_PACK_VERSION}` in `RuntimeFrameworkVersion` and both `TargetingPackVersion` attributes (the XML has no other `$`, backtick or backslash). The CARD-0913 comment says the value comes from the Dockerfile ARG through the environment. |
| `c849_fixture_masked_pack` (`c590-remote.sh:4287`) | `target=/usr/share/dotnet/packs/$pack/9.0.20` | `pin="$(c849_pack_pin)"` or `printf 'ImagePackPinUnreadable\n'; return 2`; `target=/usr/share/dotnet/packs/$pack/$pin`. |
| `c849_fixture_warm_probe` (`c590-remote.sh:4855`) | no pin env | adds `-e NET9_PACK_VERSION="$pin" -e SDK_IMAGE_TAG="$tag"` from the two helpers (unreadable: `printf 'ImagePackPinUnreadable\n'; return 2`). |
| `net9-offline` row (`verify-codex-image.sh:221-281`) | literal path, three XML literals, `10.0.401` twice | pin check after the emptiness checks (`ImagePackPinMissing`); `path="/usr/share/dotnet/packs/$pack/${NET9_PACK_VERSION}"`; `SdkTagMissing`; `case "$sdk" in "$SDK_IMAGE_TAG"\|"$SDK_IMAGE_TAG".*)` else `SdkTagMismatch:<actual>`; exactly one `--list-sdks` line (`SdkCountInvalid:<n>`) equal to `$sdk` (`SdkListMismatch:<actual>`); project heredoc unquoted with `${NET9_PACK_VERSION}` in the three places; ok detail unchanged (`SDK=$sdk ...`). |
| `scripts/verify-card0660-codex-image.ps1` | no env | reads the Dockerfile once with `(?m)^ARG NET9_PACK_VERSION=(\d+\.\d+\.\d+)\r?$` and `(?m)^FROM mcr\.microsoft\.com/dotnet/sdk:([0-9][0-9A-Za-z.-]*) AS build\r?$`; a miss is `Exit-Qualification 2`; `Invoke-Probe 'net9-offline'` adds `--env NET9_PACK_VERSION=<pin>` and `--env SDK_IMAGE_TAG=<tag>`. |
| `scripts/fixtures/seed-smoke-targeting-packs.ps1` | builds the extracted XML verbatim | reads the ARG with the same regex, replaces `${NET9_PACK_VERSION}` in the extracted project, throws if the ARG is missing or a `${` survives; evidence copies the substituted project. |
| `docs/docker-stack.md:517-519` | "9.0.20 ... SDK remains 10.0.401, selected by global.json" | the packs are at the version `ARG NET9_PACK_VERSION` declares (9.0.20 when written); the image SDK is the floating `sdk:10.0` tag's resolution (10.0.402 observed 2026-10-18), accepted by `global.json` 10.0.204/latestMinor; every smoke and probe consumer receives the pin and the tag from that Dockerfile through `NET9_PACK_VERSION` and `SDK_IMAGE_TAG`, and the SDK's bundled net9 defaults (9.0.21 on 10.0.402) are deliberately not what the smoke selects. |

Witness fixtures (S1):

| Fixture | Change |
|---|---|
| `tests/Antiphon.Tests/Scripts/SeedSmokeTargetingPackContractTests.cs` | `Net9_smoke_reference_selects_the_image_targeting_pack` (4 arguments, name kept): the ARG still must match `(\d+\.\d+\.\d+)`; the net9 project's last `FrameworkReference` for the framework must carry `TargetingPackVersion="${NET9_PACK_VERSION}"` and the project's `RuntimeFrameworkVersion` must be `${NET9_PACK_VERSION}`. New `Net9_smoke_carries_no_version_literal(scriptPath, block)` with arguments `(scripts/c590-remote.sh, c849_smoke)`, `(scripts/c590-remote.sh, c849_fixture_masked_pack)`, `(docker/session-runner-grok/verify-codex-image.sh, net9-offline)`: extract the function (first `name() {` to the next bare `}`) or the case arm (`  net9-offline)` to the next `    ;;`) and assert no `\b\d+\.\d+\.\d+\b` appears, listing the matches. Portable text tests, `Category("Unit")`. |
| `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` | New `C1352_Pack_pin_and_sdk_tag_read_the_image_dockerfile(variant)` with `valid`, `missing-arg`, `malformed-arg`, `missing-from`: a `C1066FixtureHarness("c849_pack_pin", "c849_sdk_tag")` script writes a Dockerfile under `$CHECKOUT/docker/session-runner-grok/` (`ARG NET9_PACK_VERSION=9.0.77` and `FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`, with CRLF line endings in the valid case to prove the CR strip) and expects `9.0.77`/`10.0` or a non-zero return with empty output. New `C1352_Smoke_launchers_pass_the_pin_environment`: `Block(Remote(), "c849_smoke")` contains `-e NET9_PACK_VERSION="$pin"` on its `docker exec` line and `Block(Remote(), "c849_fixture_warm_probe")` contains `-e NET9_PACK_VERSION="$pin" -e SDK_IMAGE_TAG="$tag"`. `C1066FixtureHarness` gains the Dockerfile materialisation (`9.0.77`, tag `10.0`) so the three existing `C1066_Fixture_warm_probe_uses_argument_log_under_nounset` variants run with the two helpers in their function list; their fake `docker()` additionally logs `$*` and the test asserts `NET9_PACK_VERSION=9.0.77` and `SDK_IMAGE_TAG=10.0` were passed. |
| `scripts/fixtures/c913-image-contract.sh` (`probe` mode) | exports `NET9_PACK_VERSION=9.0.77 SDK_IMAGE_TAG=10.0`; `fresh()` and the `post-cache` fault use `$NET9_PACK_VERSION`; the fake `dotnet` prints `10.0.402` for `--version` and `10.0.402 [/usr/share/dotnet/sdk]` for `--list-sdks` (an `SDK_FAULT` switch makes it print `9.0.318`, or two lines); the project-contract tokens add `<RuntimeFrameworkVersion>9.0.77</RuntimeFrameworkVersion>` and both `TargetingPackVersion="9.0.77"` attributes; new controls `pin-unset-refused`, `pin-malformed-refused` (`9.0`), `sdk-tag-unset-refused`, `sdk-tag-mismatch-refused`, `sdk-count-refused`, each failing before `restore` with the named reason; the deployment smoke section captures the project from the fake `restore` and checks the same three `9.0.77` tokens, and `NET9_PACK_VERSION` joins the `smoke-environment-refused` loop (a `/foreign` value is malformed and must be refused before the driver). New `native` mode: remap only `/c660-home`, the packages and scratch paths, keep the real `/usr/share/dotnet` and real `dotnet`, fake only `id`, export both values from the repo Dockerfile with the production `sed` expressions, run `net9-offline`, require exit 0 and one `C660_ROW net9-offline ok SDK=` line, print `PASS native-row-ok SDK=<actual>`. |
| `scripts/fixtures/c913-image-wrapper.ps1` | reads the real Dockerfile with the two regexes and checks the `net9-offline` probe call carries `NET9_PACK_VERSION=<pin>` and `SDK_IMAGE_TAG=<tag>` after `--env` (`probe-pin-env`), and that no other row carries them. |
| `scripts/fixtures/c1192-workspace-contract.sh` (`apphost` mode) | defines `c849_pack_pin() { printf '%s' 9.0.77; }` after sourcing the extracted functions and before `c849_fixture_apphost`; `apphost_smoke` and the `mask-exact-pack` case expect `/usr/share/dotnet/packs/<pack>/9.0.77`; the `pack=` strip uses the same value. |

## Slices

- S1 Witnesses first: commit and push the five fixture/test files above. CP-1 to
  CP-4 run against this commit and must be red for the stated reasons.
- S2 Production and docs: `scripts/c590-remote.sh`,
  `docker/session-runner-grok/verify-codex-image.sh`,
  `scripts/verify-card0660-codex-image.ps1`,
  `scripts/fixtures/seed-smoke-targeting-packs.ps1`, `docs/docker-stack.md`. CP-5 to
  CP-11 run against the exact committed SHA.

Out of scope, by decision: the Dockerfile, the pin-pair test literal, the legacy
cache payload paths, CARD-0916's shape change, any image build, the previous card's
mutation ledger (owned by its SourceLanding Mutation; see Handoff).

## Verification design

| ID | Ordinary witness | Acceptance |
|---|---|---|
| V-1 | `SeedSmokeTargetingPackContractTests.Net9_smoke_reference_selects_the_image_targeting_pack` (4 arguments) | Red at S1 (attribute and element are `9.0.20`, not the token); green at S2. |
| V-2 | `SeedSmokeTargetingPackContractTests.Net9_smoke_carries_no_version_literal` (3 arguments) | Red at S1 (`9.0.20` in both functions, `9.0.20` and `10.0.401` in the row); green at S2 with no three-part literal in any block. |
| V-3 | `CodexRunnerImageContractTests.C913_Net9_probe_rejects_warm_caches_and_missing_packs` (fixture `probe` mode) | Red at S1: `FAIL native-run-required [case=probe-success]` with `ImagePackMissing:Microsoft.NETCore.App.Host.linux-x64` because the row probes `/9.0.20` in a tree built at `/9.0.77`. Green at S2 with the five new PASS lines and the deployment-smoke token checks. |
| V-4 | `RemoteScriptContractTests.C1352_Pack_pin_and_sdk_tag_read_the_image_dockerfile` (4 variants) | Red at S1 (functions missing, `Block` fails); green at S2. |
| V-5 | `CodexRunnerImageContractTests.C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` (fixture wrapper) | Red at S1 (`FAIL probe-pin-env`); green at S2. |
| V-6 | `RemoteScriptContractTests.C1066_Fixture_warm_probe_uses_argument_log_under_nounset` (3 variants) and `C1352_Smoke_launchers_pass_the_pin_environment` | Red at S1 (no pin env in the logged `docker` arguments; static text absent); green at S2 with the warm refusal unchanged. |
| V-7 | `RemoteScriptContractTests.C1192_Apphost_mask_is_an_empty_exact_volume_mount` | Red at S1 (`mask-exact-pack=0`, mount still `/9.0.20`); green at S2. |
| V-8 | `bash scripts/fixtures/c913-image-contract.sh native .` on the standing image SDK | Red at S1: `C660_ROW net9-offline fail default SDK is not 10.0.401`. Green at S2: `PASS native-row-ok SDK=10.0.402`, restore/build/native exit 0, empty packages. |
| V-9 | `pwsh -NoProfile -File scripts/fixtures/seed-smoke-targeting-packs.ps1` | Two substituted projects restore, build and run offline with the real SDK; `PASS ... packages=empty` twice. |
| R-1 | Whole `Antiphon.Tests` Unit lane | Required regression lane; report every failure and skip, inherited red checked method-scoped at the base. |
| R-2 | `bash -n` of every edited shell file (the helper does `c590-remote.sh` and the row; CP-11 does the two fixtures) | Valid syntax. |
| R-3 | `CodexRunnerImageContractTests.Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing` | Unchanged pin-pair and `archive` mode still green (D-6). |

Positive controls pending for the caller-commissioned SourceLanding Mutation of this
card (method-scoped, bound to the landed SHA):

| ID | Variant | Mutation | Detecting filter |
|---|---|---|---|
| PC-1 | smoke-path; smoke-runtime; row-path; row-runtime; row-targeting; mask-target | Put `9.0.20` back in that one place | `/*/*/SeedSmokeTargetingPackContractTests/Net9_smoke_carries_no_version_literal` (every variant) and, for the XML variants, `/*/*/SeedSmokeTargetingPackContractTests/Net9_smoke_reference_selects_the_image_targeting_pack` |
| PC-2 | wrapper | Drop one `--env` from `Invoke-Probe` | `/*/*/CodexRunnerImageContractTests/C913_Image_wrapper_isolates_mounts_and_qualifies_receipts` |
| PC-3 | smoke-exec; warm-probe | Drop `-e NET9_PACK_VERSION` from the `docker exec` / `docker run` line | `/*/*/RemoteScriptContractTests/C1352_Smoke_launchers_pass_the_pin_environment`; `/*/*/RemoteScriptContractTests/C1066_Fixture_warm_probe_uses_argument_log_under_nounset` |
| PC-4 | sdk-exact | Replace the tag comparison with `[ "$sdk" = 10.0.402 ]` | `/*/*/CodexRunnerImageContractTests/C913_Net9_probe_rejects_warm_caches_and_missing_packs` (`sdk-tag-mismatch-refused` is not the reason, `pin`/`tag` unset cases still refuse) and the `native` row on a host whose SDK differs |
| PC-5 | reader | Make `c849_pack_pin` ignore the CR strip or print the first ARG line verbatim | `/*/*/RemoteScriptContractTests/C1352_Pack_pin_and_sdk_tag_read_the_image_dockerfile` |

A Dockerfile ARG bump with no consumer change is no longer a defect class: every
consumer follows it. What such a bump still needs is an image rebuilt from that
Dockerfile; until then the native and live rows fail closed at `ImagePackMissing`,
which is the intended verdict, not a control.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1352-red/` | pin-literal-red | `/*/*/SeedSmokeTargetingPackContractTests/*` | V-1, V-2 red-first | 7 executed, 7 failed: 4 on `9.0.20` not being the token, 3 on literals present (`9.0.20`; `9.0.20`; `9.0.20` and `10.0.401`) | 7 | 6 |
| CP-2 | S1 | CP-1 | consumer-red | `/*/*/CodexRunnerImageContractTests/(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)\|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*)` | V-3, V-5 red-first | 2 executed, 2 failed: fixture `FAIL native-run-required [case=probe-success]` naming `ImagePackMissing`, wrapper `FAIL probe-pin-env` | 2 | 4 |
| CP-3 | S1 | CP-1 | remote-red | `/*/*/RemoteScriptContractTests/(C1352_Pack_pin_and_sdk_tag_read_the_image_dockerfile*)\|(C1352_Smoke_launchers_pass_the_pin_environment*)\|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset*)\|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)` | V-4, V-6, V-7 red-first | 9 executed, 9 failed: 4 missing functions, 1 static text absent, 3 pin env absent in the logged arguments, 1 `mask-exact-pack=0` | 9 | 5 |
| CP-4 | S1 | n/a | native-row-red | `bash scripts/fixtures/c913-image-contract.sh native .` | V-8 red-first | exit 1 with `C660_ROW net9-offline fail default SDK is not 10.0.401` and no `restore` in the trace | n/a | 2 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1352-green/` | pin-green | `/*/*/SeedSmokeTargetingPackContractTests/*` | V-1, V-2 | all 7, 0 failed/skipped | 7 | 6 |
| CP-6 | S2 | CP-5 | consumer-green | `/*/*/CodexRunnerImageContractTests/(C913_Net9_probe_rejects_warm_caches_and_missing_packs*)\|(C913_Image_wrapper_isolates_mounts_and_qualifies_receipts*)\|(Net9_packs_are_pinned_verified_before_extraction_and_available_to_session_testing*)` | V-3, V-5, R-3 | all 3, 0 failed/skipped; the fixture output lists `PASS pin-unset-refused`, `PASS pin-malformed-refused`, `PASS sdk-tag-unset-refused`, `PASS sdk-tag-mismatch-refused`, `PASS sdk-count-refused`, `PASS probe-boundaries-complete`; the wrapper output lists `PASS probe-pin-env` | 3 | 4 |
| CP-7 | S2 | CP-5 | remote-green | `/*/*/RemoteScriptContractTests/(C1352_Pack_pin_and_sdk_tag_read_the_image_dockerfile*)\|(C1352_Smoke_launchers_pass_the_pin_environment*)\|(C1066_Fixture_warm_probe_uses_argument_log_under_nounset*)\|(C1192_Apphost_mask_is_an_empty_exact_volume_mount*)` | V-4, V-6, V-7 | all 9, 0 failed/skipped | 9 | 5 |
| CP-8 | S2 | CP-5 | unit-lane | `/*/*/*/*[Category=Unit]` | R-1 | >= 1 executed, report every failure/skip | 1 | 20 |
| CP-9 | S2 | n/a | native-row | `bash scripts/fixtures/c913-image-contract.sh native .` | V-8 | exit 0, one `C660_ROW net9-offline ok SDK=10.0.402 ...` line, `PASS native-row-ok SDK=10.0.402`, empty packages | n/a | 2 |
| CP-10 | S2 | n/a | native-smoke | `pwsh -NoProfile -File scripts/fixtures/seed-smoke-targeting-packs.ps1` | V-9, R-2 | 2 restores and 2 builds with 0 errors, 2 exact native stdout witnesses, 2 empty package inventories, 2 shell syntax checks, substituted projects carry `9.0.20` in all three places | n/a | 3 |
| CP-11 | S2 | n/a | fixture-syntax | `bash -n scripts/fixtures/c913-image-contract.sh && bash -n scripts/fixtures/c1192-workspace-contract.sh` | R-2 | exit 0 | n/a | 1 |

Each OR operand in a combined filter carries its own parentheses and trailing
wildcard (CARD-0403); verify the executed roster in the fresh TRX, never the exit
code. Non-TUnit rows run under `pwsh -NoProfile -File scripts/build-slot.ps1 -Label
<group> -- <command>`; the native rows restore offline against the installed packs
and must not populate the private packages directory they create. The `native` row
and the helper need the Linux image SDK; on Windows the shell-backed tests skip or
run under WSL as they do today.

### Cost

Ordinary floor: 58 minutes from the table (two isolated test builds, one Unit lane,
two native restores). Authoring: about 75 minutes for S1 (two test files, three
fixtures) and about 60 minutes for S2 (two shell scripts, two PowerShell scripts, one
doc paragraph). Code estimate about 3.5 hours including inherited-red triage at the
base; Review about 1 hour; SourceLanding Mutation about 1 hour for the five
method-scoped controls. No image build, no full-assembly run, no repetitions.

Sequencing note for Code: CP-1 to CP-4 are red rows; the checkpoint tool's expected
counts for them are failures, as in the previous card's CP-1. Commit S1 before CP-1
and S2 before CP-5; do not edit source while a row is in flight.

## Handoff

Next stage: Code, on this plan's committed SHA, running `### Checkpoints` as the
closed list. Restart: none. Land: master only (the card says master-only; the rc
line picks it up with the next cut). After land the caller qualifies the live row
with `verify-card0660-codex-image.ps1 -Target session-testing` against a fresh
build of the landed SHA, then runs the Fixture/Inventory acceptance already named
on CARD-1352.

Two notes for other owners, not actions in this plan:

- The pending ledger `docs/superpowers/mutations/2026-10-19-seed-smoke-targeting-packs.md`
  (SourceLanding Mutation of task c8c9f3c5) keeps PC-1 valid under the kept method
  name; its PC-2 ("change the ARG only") cannot go red once this lands and should be
  replaced by this plan's PC-1 if that Mutation binds a tip that includes this card.
- CARD-0916: measured here on SDK 10.0.402 with the image packs, dropping
  `RuntimeFrameworkVersion` fails offline with `NU1100 Microsoft.NETCore.App.Host.linux-x64 (= 9.0.21)`;
  the default net9 shape needs the apphost pack version selected some other way
  before that card's ask is possible, and `TargetingPackVersion` must stay.
