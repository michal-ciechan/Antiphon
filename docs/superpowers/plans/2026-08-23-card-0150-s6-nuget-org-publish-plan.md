# CARD-0150 S6 — NuGet.org publish + feed proof: design

**Date:** 2026-08-23 · **Card:** CARD-0150 (`9f480f6f-54da-476a-8aee-f90ef4f4140f`) ·
**Status:** design (no implementation in this pass) ·
**Verified against:** `4909bbf` (branch `feat/card-task-511a1b5f`, master-based). Facts about the
current workflow, csprojs and feeds are direct reads of this tree plus the S6 investigation
(task `cd78ef3d`, recorded on the card 2026-08-23) — that investigation's findings are taken as
given here, not re-derived.

**Parent plan:** `docs/superpowers/plans/2026-08-23-card-0150-public-messaging-contract-plan.md`
(§3.3 versioning policy, §5 D1). D1 is operator-confirmed: **push to NuGet.org now** at the
already-cut `1.0.0` (`src/Messaging.Pack.props:6`), reserve the `Antiphon.*` prefix in parallel by
manual email to account@nuget.org (no API for reservation exists).

---

## Decisions up front

| # | Question | Decision |
|---|----------|----------|
| D-A | GitHub Packages disposition | **Keep dual-publishing.** GH Packages stays as-is; NuGet.org is added alongside, and becomes the *documented* feed. |
| D-B | NuGet.org pack-list scope | **The 4 contract-surface packages only**: `Antiphon.Messaging`, `.Client`, `.Client.Testing`, `.Gateway`. Slack/Telegram stay GH-only; FakeGateway's dead pack line is **removed** from the workflow. |
| D-C | Secret name + push shape | **`NUGET_API_KEY`**, passed inline as an explicit `--api-key "${{ secrets.NUGET_API_KEY }}"` — never exported as an env var. A guard step fails red with a named message if the secret is unset. |
| D-D | Feed-proof isolation + retry | Separate `prove-nuget-org` job: `<clear/>`-scoped nuget.config (nuget.org only), fresh `NUGET_PACKAGES` dir, restore of `samples/EchoGateway -p:UsePublishedPackages=true` retried **20 × 180 s** (~60 min ceiling, matching worst-case first-publish indexing), then `--no-restore` build + `--self-test` run. |
| D-E | Trigger scope | Add `samples/EchoGateway/**` to the paths filter. The proof runs on **every** workflow trigger (steady-state cost ≈ one restore attempt). |
| D-F | Verification | No YAML unit-testing forced into the suite; instead (1) a small `PublishWorkflowSanityTests` file-pin in `Antiphon.Messaging.Tests` guarding the pack list against the FakeGateway silent-no-op genre, and (2) a scripted first-run validation choreography (§7). |

Also in scope as a correctness fix found en route: the workflow installs SDK **9.0.x**
(`.github/workflows/publish-nuget.yml:33`) while `global.json` demands `10.0.204` +
`rollForward: latestMinor` — today's runs only succeed because ubuntu-latest happens to preinstall
a .NET 10 SDK. Pin `dotnet-version: "10.0.x"` (§4).

---

## 1. D-A — GitHub Packages: keep dual-publishing

**Keep it.** Reasons:

1. **It costs nothing to keep.** The push uses the automatic `GITHUB_TOKEN`
   (`publish-nuget.yml:49`) — no secret to rotate, no account to maintain — and
   `--skip-duplicate` makes every re-run of an already-published version a no-op.
2. **Turning it off forces a migration this card doesn't need.** The three private packages
   (`Messaging`, `Client`, `Client.Testing`) have at least one live consumer (the
   `school_revision` instance restores the Client pair from GH Packages with a PAT). Removing the
   GH push, or relying on nuget.org alone, makes that consumer's next restore depend on a feed
   switch nobody has scheduled.
3. **It is the fallback while nuget.org is young.** First-publish indexing on nuget.org is
   documented at <15 min, up to ~1 hr, and validation can fail outright; during that window GH
   Packages is the only feed the packages verifiably exist on.
4. **The dual-publish cannot drift into inconsistency.** Both feeds are fed from the same `out/`
   pack in the same job at the same `Messaging.Pack.props` version; there is no second version
   source to skew.

What changes anyway: the **documentation pointer flips**. `samples/EchoGateway/EchoGateway.csproj:10-11`
and `docs/messaging/build-your-own-gateway.md` currently name GH Packages as the live feed with
"NuGet.org pending"; after the first successful publish they name nuget.org as the feed a third
party uses (no PAT needed — the GH feed's private core packages made third-party restore
impossible there anyway) and GH Packages as the first-party mirror. Revisiting GH Packages
disposition later (e.g. a deprecation window once nuget.org is proven) is explicitly *not* part of
this card.

## 2. D-B — nuget.org scope: the 4 contract-surface packages

Push **`Antiphon.Messaging`, `Antiphon.Messaging.Client`, `Antiphon.Messaging.Client.Testing`,
`Antiphon.Messaging.Gateway`** to nuget.org. Not Slack, not Telegram, not FakeGateway.

- The nuget.org feed exists for the CARD-0150 deliverable: a third party building a gateway
  (`Messaging` + `Gateway`) or an application-side consumer (`Client` + `Client.Testing`). Those
  four are the documented, API-baselined surface (`src/Messaging.Pack.props:14-16` enforces
  PublicAPI baselines on exactly the packable set).
- **Slack/Telegram are first-party gateway *implementations***, deployed as part of `am-service`
  — not the contract. Publishing them to nuget.org creates a public semver/support obligation for
  internals that can still change freely on GH Packages. If the operator later wants them public,
  it is a one-line addition each to the push list, and the prefix reservation will already cover
  the IDs. They keep being packed and pushed to GH Packages unchanged.
- **FakeGateway stays out everywhere, and its dead pack line is removed** (`publish-nuget.yml:41`).
  The investigation confirmed the line is a silent no-op — `Antiphon.Messaging.FakeGateway.csproj`
  is `Sdk="Microsoft.NET.Sdk.Web"` with `PackAsTool=true` but `IsPackable` never set, so `dotnet
  pack` produces no nupkg (404 on GH Packages, warning in the S4 CI log). Removing it is
  behaviourally a pure no-op *and* it is dead intent in the exact file this change edits; leaving
  it standing invites someone "fixing" it by adding `IsPackable=true` and accidentally publishing
  a dev tool. Its entry in the trigger paths filter is **also removed** — a FakeGateway change no
  longer has any effect on this workflow. (FakeGateway the *project* is untouched; it ships inside
  the dev stack, not as a package.)

Dependency closure check: `Gateway` → `Messaging`; `Client` → `Messaging`; `Client.Testing` →
`Client` (csproj ProjectReferences, verified). The 4-package set is closed — every intra-repo
dependency of a pushed package is itself pushed, so a nuget.org-only restore can resolve the whole
graph. (nuget.org does not require dependencies to pre-exist at push time, so push order within
the four is irrelevant.)

## 3. D-C — secret `NUGET_API_KEY`, explicit `--api-key`, loud guard

**Name: `NUGET_API_KEY`.** No naming convention exists in this repo yet (`GITHUB_TOKEN` is the
only secret referenced in either workflow), so pick the ecosystem-conventional name: it is what
the investigation suggested, what thousands of published workflows use, and there is no second
NuGet push target to disambiguate from (GH Packages authenticates with `GITHUB_TOKEN`). The extra
precision of `NUGET_ORG_API_KEY` buys nothing here.

**Wiring:** the value is interpolated directly into the push command —

```yaml
- name: Require NuGet.org API key
  if: ${{ secrets.NUGET_API_KEY == '' }}
  run: |
    echo "::error::NUGET_API_KEY repo secret is not set. Set it from the Bitwarden item 'NuGet.org - Antiphon publish key' (card CARD-0150 secret-handling note), then re-run."
    exit 1
- name: Push to NuGet.org
  run: |
    VERSION=$(grep -oPm1 '(?<=<Version>)[^<]+' src/Messaging.Pack.props)
    for id in Antiphon.Messaging Antiphon.Messaging.Client Antiphon.Messaging.Client.Testing Antiphon.Messaging.Gateway; do
      dotnet nuget push "out/$id.$VERSION.nupkg" \
        --source "https://api.nuget.org/v3/index.json" \
        --api-key "${{ secrets.NUGET_API_KEY }}" \
        --skip-duplicate
    done
```

Design points, each load-bearing:

- **Explicit `--api-key`, never the env-var shortcut.** The SDK's automatic `NUGET_API_KEY`
  env-var pickup exists only from SDK 10.0.300+, and this workflow's SDK version has already
  drifted once (9.0.x pinned, 10.0.400 actually used — §4). An inline secret has no SDK-version
  dependency at all, and because it is never exported into the job environment, no *other* dotnet
  invocation in the job can silently start depending on it either.
- **Explicit per-ID filenames, not `out/*.nupkg`.** The `out/` directory contains six nupkgs
  (GH Packages gets them all); a wildcard would push Slack/Telegram to nuget.org. Naming
  `$id.$VERSION.nupkg` also fails loudly if a pack silently stopped producing a file — the exact
  failure mode FakeGateway just demonstrated — because `dotnet nuget push` errors on a
  non-existent path. The version comes from the single version source
  (`src/Messaging.Pack.props`), so the push list cannot skew from what was packed.
- **Guard fails red, never skips.** The dispatch constraint is that the YAML must be able to land
  before the key exists — it can: nothing in this design requires the secret at merge time. But a
  run that fires without the key **fails on a step whose name says exactly what to do**, rather
  than skipping green. A green skip is the silent-no-op genre again: the workflow would pass
  forever with the key never set. One red run before the secret lands is honest and cheap; the
  intended ordering (§8) sets the secret first anyway. (Step-level `if:` may reference the
  `secrets` context; job-level `if:` may not — hence guard-as-step.)
- `--skip-duplicate` behaves on nuget.org as on GH Packages (409 → warning), so re-runs and
  the every-trigger proof (§6) stay no-ops for already-published versions.

Secret provisioning itself is the already-decided card track, not this design: operator creates
the key → Bitwarden via ClaudeBot (`NuGet.org - Antiphon publish key`) → a dispatched agent sets
the repo secret from the vault (`bw get … | gh secret set NUGET_API_KEY --repo michal-ciechan/Antiphon`),
never printing the value.

## 4. Workflow restructure (including the SDK pin fix)

`publish-nuget.yml` becomes two jobs:

**Job `publish`** (existing job, amended):
1. checkout; `setup-dotnet` with `dotnet-version: "10.0.x"` — matching `global.json`
   (`10.0.204` + `latestMinor`) instead of depending on the runner's preinstalled SDK set. Same
   fix in the new proof job. (`ci.yml`'s identical 9.0.x pin is out of scope but noted for a
   follow-up.)
2. `dotnet run --project tests/Antiphon.Messaging.Tests` (unchanged gate).
3. Pack the **six** real packages to `out/` (current list minus the FakeGateway line).
4. Push `out/*.nupkg` to GH Packages (unchanged).
5. Guard + push the four to nuget.org (§3).

**Job `prove-nuget-org`** (`needs: publish`, so a failed guard/push skips it): §5.

Header comment at the top of the file is rewritten to describe the dual-feed reality and the
proof job.

## 5. D-D — the feed-proof job: isolation and retry

The proof must fail if, and only if, `samples/EchoGateway` cannot be built **from nuget.org
alone** at the version just pushed. Three ingredients:

**(a) Source isolation.** The repo has no `nuget.config` anywhere (verified by glob), so restore
inherits ambient machine config. The proof writes its own and pins everything to it:

```bash
mkdir -p "$RUNNER_TEMP/nuget-proof"
cat > "$RUNNER_TEMP/nuget-proof/nuget.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
```

`<clear/>` removes every inherited source, so GH Packages (or anything a future runner image
adds) cannot answer the restore.

**(b) Cache isolation.** Job-level `env: NUGET_PACKAGES: ${{ runner.temp }}/nuget-proof/packages`
— a fresh global-packages folder, so no earlier restore on the runner (or a future
`actions/cache` addition) can satisfy the Antiphon packages from disk. It applies to the
`--no-restore` build too (the assets file points into it). Being a separate job on a separate
runner from `publish` already helps, but the env pin makes the isolation explicit rather than
incidental.

**(c) Version under proof.** `samples/EchoGateway/EchoGateway.csproj:37` hard-codes
`Version="1.0.0"`; left alone, the proof would keep proving 1.0.0 after a bump to 1.1.0. Small
csproj change, part of this slice: introduce a defaulted property —

```xml
<AntiphonMessagingVersion Condition="'$(AntiphonMessagingVersion)' == ''">1.0.0</AntiphonMessagingVersion>
…
<PackageReference Include="Antiphon.Messaging.Gateway" Version="$(AntiphonMessagingVersion)" />
```

— and CI passes `-p:AntiphonMessagingVersion=$VERSION` with `$VERSION` grep'd from
`Messaging.Pack.props` exactly as in §3. Local `-p:UsePublishedPackages=true` behaviour is
unchanged (defaults to the last released version).

**(d) The retry loop.** First-publish indexing of a brand-new package ID is documented <15 min,
up to ~1 hr — and all four IDs are brand new to nuget.org. Shape:

- **Interval: fixed 180 s. Attempts: 20. Ceiling: ~60 min** of waiting (matching the documented
  worst case), first attempt immediate. Fixed interval rather than exponential backoff: indexing
  is a queue that completes once, there is nothing to be gentle with, and a fixed interval makes
  "how long until it gives up" arithmetic instead of a series.
- Each attempt: `dotnet restore samples/EchoGateway -p:UsePublishedPackages=true
  -p:AntiphonMessagingVersion=$VERSION --configfile "$RUNNER_TEMP/nuget-proof/nuget.config"`.
  On failure, log `attempt i/20 — not yet restorable from nuget.org (fresh IDs index in <15 min,
  worst case ~1 hr); retrying in 180s` and sleep.
- On success: `dotnet build samples/EchoGateway -c Release -p:UsePublishedPackages=true
  -p:AntiphonMessagingVersion=$VERSION --no-restore`, then run the built dll with `--self-test`
  (the Kafka-free adapter round-trip that already exists) — proving the restored bits *run*, not
  just resolve. `dotnet <dll>` directly, not `dotnet run`, which would re-restore outside the
  pinned config.
- **Failure looks like:** the loop exhausts, the step emits
  `::error::Antiphon packages not restorable from nuget.org after ~60 min. Check
  https://www.nuget.org/packages/Antiphon.Messaging — first-publish validation may have failed or
  indexing may be unusually slow. Packages ARE pushed (the publish job succeeded); once they show
  as listed, re-run this job.` and exits 1. Job carries `timeout-minutes: 75` as the hard
  backstop. The proof failing does **not** roll anything back — nuget.org has no unpush, and the
  publish job's result stands; the proof is verification, not a transaction.
- Steady state (version already indexed): the loop exits on attempt 1 and the job costs about a
  minute — which is what makes running it on every trigger (§6) cheap.

## 6. D-E — trigger scope

- **Add `samples/EchoGateway/**` to the paths filter.** The sample is now load-bearing CI
  material: a sample change can break published-mode restore specifically (e.g. using an API not
  in the released version) and today nothing would notice until the next unrelated publish.
  In-repo (ProjectReference-mode) compile-and-test coverage of the sample already exists on every
  push via `ci.yml` (no paths filter; `Antiphon.Messaging.Tests` carries the 5 EchoGateway tests)
  — what only this workflow can prove is the published-feed mode. A sample-only trigger re-packs
  and re-pushes, all no-ops via `--skip-duplicate`, then runs the proof: total cost a few
  runner-minutes.
- **The proof runs on every workflow trigger**, not only after a version bump. Detecting "did
  this run actually publish anything new" to skip the proof would add state and a conditional
  whose false-negative is precisely the silent-no-op failure mode this card keeps finding; the
  retry loop already makes an already-indexed version cost one attempt. `workflow_dispatch` (kept)
  doubles as the manual re-proof lever after an indexing-timeout failure.
- Remove `src/Antiphon.Messaging.FakeGateway/**` from the filter alongside its pack line (§2).

## 7. D-F — verification and test design

**Not** YAML unit tests — this repo tests behaviour, not CI syntax, and a YAML-parsing test suite
would pin formatting rather than intent. Two mechanisms instead:

**(a) `PublishWorkflowSanityTests` in `tests/Antiphon.Messaging.Tests`** — one small file-pin
test class in the same genre as the existing committed-JSON-schema drift pin:

- For every `dotnet pack src/<X>.csproj` line in `publish-nuget.yml`: the csproj exists, and is
  actually packable — specifically, **not** `Sdk="Microsoft.NET.Sdk.Web"` without an explicit
  `<IsPackable>true</IsPackable>` (the exact FakeGateway silent-no-op shape).
- Every ID in the nuget.org push list appears in the pack list, and its intra-repo dependency
  closure (ProjectReferences that are themselves packed projects) is contained in the nuget.org
  push list — pinning §2's closure property so a future `Gateway → NewProject` reference can't
  silently produce an unrestorable feed.

  These are string/regex-level reads of the workflow + csproj files — crude, but they turn the
  two silent failure modes this investigation actually found into red tests.

**(b) First-run validation choreography** (manual, scripted in the build dispatch's report):

1. Precondition: `NUGET_API_KEY` repo secret set (§8 ordering). Merge the slice; the workflow
   fires (its own path is in the filter).
2. Watch the `publish` job: 6 nupkgs packed (no FakeGateway warning any more), GH push unchanged
   (all skip-duplicate no-ops at 1.0.0), nuget.org push shows 4 pushes accepted.
3. Watch `prove-nuget-org`: expect **real retries** on this first run (fresh IDs); it must go
   green within the ceiling. If it times out, check the package pages, then re-run the job —
   never re-push manually.
4. Manually confirm the four package pages exist on nuget.org under the operator's account and
   the READMEs/license rendered (`Messaging.Pack.props` metadata).
5. `workflow_dispatch` a second run end-to-end: everything no-ops, proof passes on attempt 1 —
   this is the idempotency proof, and the shape every future non-bump run takes.
6. Flip the doc pointers (§1) in the same slice or an immediate follow-up commit.

There is deliberately **no dry-run mode**: nuget.org has no staging feed worth wiring (int.
nugettest.org needs a separate account and proves nothing about the real IDs), and a "dry run"
flag in the workflow would be one more conditional whose untested branch is the real one. The
guard step (§3) is what a premature run hits, and it fails with instructions.

## 8. Slices and ordering

- **S6a (operator + ClaudeBot, already specified on the card):** NuGet.org account + API key →
  Bitwarden → `gh secret set NUGET_API_KEY`. Prefix-reservation email sent in parallel; the push
  does not wait for reservation (pushing is itself what claims the four real IDs — D1's point).
- **S6b (build slice, this design):** workflow restructure (§§2-6), EchoGateway csproj version
  property (§5c), `PublishWorkflowSanityTests` (§7a), doc-pointer flips (§1), workflow header
  comment. All one commit-able unit; mergeable independent of S6a, but merge *after* S6a lands to
  avoid the one red guard run.
- **S6c:** first-run choreography (§7b) + card close-out note.

Risks accepted: pack-time resolution of the floating `9.*` Extensions dependency ranges in the
Gateway nupkg has never been proven against a from-feed restore — that is precisely what the
proof job exists to catch, on its first run, loudly.
