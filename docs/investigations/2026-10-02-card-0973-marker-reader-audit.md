# CARD-0973: marker-reader audit and cold-image cleanup diagnosis

The caller supplied the mandatory read-only live capture in
`.antiphon/inbox/EVIDENCE-0973.md`, resolving the initial access blocker below.
It proves `deploy-parent` deleted the cold marker's seed image, so the later
`docker image inspect` failed at base lines 2030-2031. Every format predicate
passed. The marker has schema=2, kind=cold, cold=true, source SHA
`1f43624d1353ea59338a9c4294912cb83e47fc2e`, image
`sha256:09fe5601ba1d88a8658e7d78d1818a16b15c1cf1d39cc859601fcdaedc364bc1`, and
the three fixed volume names. stat was mc:mc/600, 287 bytes, mtime
2026-10-02 08:39:21Z. The deployed script hash was
`870e226e89ae78adcb9147f6b745c581d210d3112a68fcdd31aad58c785da782`, identical
to base/deploy source. No additional server2 access was attempted after that capture.

Red evidence on production base plus committed fixtures: the rolling T-20 assertion
reports CacheSeedMarkerInvalid after fake parent image cleanup; CP-973-red executes
12 results, all 12 fail at the intended reader assertions, 0 skipped. An earlier
assertion-overload compilation failure and short-SHA input refusal do not count
as red evidence. Production now checks cold seed-image format without requiring
that historical image to exist. Helper fallback uses an identified current main
image, and cold Retired records a locally available helper identity. Full-marker
recovery/hash/retained-image validation and maintenance rules are unchanged.

The following sections preserve the original base-source audit. Final coverage and
rerun commands are in
`docs/superpowers/plans/2026-10-02-card-0973-cold-marker-verification-fix.md`.

## Evidence and blocker

The card was read using `pwsh -NoProfile -File scripts/card.ps1 get CARD-0973
-Board Antiphon`. It reports `HostCaseFailed verify-runner-caches exit=2` and a
desktop receipt at `.antiphon/rolling-server2/c7271420208e68b9/c590-result.json`
containing `accepted=false`, `diagnosis=CacheSeedMarkerInvalid`, `exit=2`.
That receipt is not present in this mirror. Its contents are card-provided evidence,
not independently read bytes.

A read-only SSH request for `stat` and `cat` of
`/home/mc/antiphon-server2/cache/seed-accepted` failed first at host-key verification.
After accepting the newly encountered host key in this container's own known-hosts
file, the same request failed with `mc@server2: Permission denied (publickey)`.
No host command ran. This container has no mc SSH identity; its documented
repo-scoped GitHub deploy key is not a host-login credential.

Initially the caller needed to supply the approved host-login path, or a read-only marker capture
from its trusted desktop. The brief requires the real marker before implementation;
substituting a fake would violate that requirement. Capture only the specified
non-secret marker and receipt, never provider homes or credential files:

```powershell
ssh mc@server2 'stat /home/mc/antiphon-server2/cache/seed-accepted; cat /home/mc/antiphon-server2/cache/seed-accepted'
Get-Content -Raw -LiteralPath .antiphon/rolling-server2/c7271420208e68b9/c590-result.json
```

## Source finding

Base `4453dab63e5005414b2ded095b674f7b0ef1919e` and reported deployment
`d81ff99ce3e3bc95a571e778081b19a68c009a17` contain identical
`scripts/c590-remote.sh` and `scripts/deploy-server2.ps1` bytes (`git diff` is empty).
Both already use `c849_require_ready allow-cold` in `case_deploy_parent` and
`case_verify_runner_caches`. The brief's suggested missing cold mode is not
established by the source.

Before the supplied live capture, the failure could not be attributed to a single source line. In
`scripts/c590-remote.sh`, the cold branch can emit `CacheSeedMarkerInvalid` at:

| Line | Exact check |
|---|---|
| 2016 | Marker must be a nonempty regular file, not a symlink. |
| 2027 | Exactly eight newline-terminated lines and unique keys; schema=2, kind=cold, cold=true; lowercase 40-digit source SHA; three exact volume names. Source SHA format is checked, not equality to the rollout SHA. |
| 2031 | Seed image must have a sha256 identity and remain locally inspectable. |
| 2034 | DockerRootDir must be available. |
| 2037 | DockerRootDir must be nonempty, not a symlink, and canonical under sudo realpath. |
| 2041 | Each of the three cache volumes must be present in the volume census. |

Additional foreign-volume/root refusals come from `c849_cold_volume_facts`.
The marker's real field names and values, file metadata, and precise failing
predicate were initially unverified. A syntactically valid marker alone may not distinguish
the host-dependent predicates. Do not weaken these guards based on the generic
diagnosis alone.

## Complete marker-reader audit

Line numbers refer to the unchanged source at the base/deployment above. Writes
are listed separately where needed to distinguish fixture markers from production.

| Reader in scripts/c590-remote.sh | Lines | Current behavior |
|---|---|---|
| `c849_image` | 1321, 1322, 1334 | Reads kind/image for helper fallback in seed, inventory, fixture, preview, reset. Cold image identity must be locally inspectable; this is not ready-marker admission. |
| `c849_volume` | 1392 | Tests marker nonemptiness before unmarked existing-volume creation checks. Final callers separately validate readiness. |
| `c849_cold_seed` | 1819, 1849 | Requires marker absence before initial proof and publication. New marker write is 1851-1855. |
| `c849_seed` | 1864, 1871, 1877, 1879, 1881 | Explicit cold creation requires absence; existing cold avoids donor lookup. SavedDonor uses full-required, ordinary Seed uses allow-cold. Full marker write is 2002-2004. |
| `c849_require_ready` | 2013-2041, 2047, 2052 | Shared file/schema/image/root/volume validation; full branch reads recovery and payload hash. Default requires full. |
| `c849_preview` | 2127, 2128 | Reads full recovery for size accounting; cold has no recovery. Maintenance context, outside redeploy/drain verification. |
| `c849_reset` | 2273-2275 | Refuses existing accepted markers; cold-format/symlink paths invoke full-required. |
| `c849_prune` | 2331, 2352, 2355, 2359, 2362 | Full-required before receipt checks and again after idle check; reads recovery, hash, donor image. |
| `c849_fixture_seed` | 2690, 2692, 2698 | Reads private fixture marker for smoke publication order, recovery and hash. Marker is redirected at 2636. |
| `case_verify_runner_caches` | 3293, 3305 | allow-cold for both runner IDs; only full reads payload hash and runs apphost smoke. |
| `case_verify_runner_caches_retired` | 3357, 3359, 3371 | allow-cold; reads seed image for retained rollback identity; only full reads hash/runs smoke. |
| `case_deploy_parent` | 3421 | allow-cold before shared-cache budget, broker and standing-runner deployment. |
| `case_deploy_temp_runner` | 3577 | allow-cold before temp deployment. |
| `case_retire_temp_runner` | 3687 | allow-cold before temp Compose removal and shared-volume retention checks. |

Other fixture-only marker assignments/writes: `c849_fixture_prepare` at 2568/2585,
`c849_fixture_unmarked_consumer` at 2729, and `c849_fixture_prune` at 3166/3175.
Their calls reuse the readers above; they do not read the live seed-accepted file.

`scripts/deploy-server2.ps1` contains no direct marker-file readers. Its host-case
invocations are ordinary Seed at 162, temp deployment at 163, temp verification
at 168, parent deployment at 204, standing verification at 209, and retirement
at 246. SavedDonor is transported only for Seed at 123. The drain-old (178) and
drain-temp (219) branches read runner status and do not read the marker or invoke
a marker-reading host case. Redeploy-old's successful host verification precedes
the admission clear at 213-214. These lifecycle/drain gates were not changed.

## Harness finding and remaining work

`scripts/fixtures/c727-fake-verify.ps1` does not read or validate any marker. Its
verify branch checks the runner ID and optional forced failure; other host cases
mostly update fixture state. `scripts/test-deploy-server2.ps1` creates no marker
in Run-C727. Thus its passing redeploy path is not evidence of real marker
compatibility, and the current fake does not prewrite a full marker either.

`C912_Cold_marker_has_distinct_validation_and_full_context_refusal` calls the
real shared validator, but labels a default/full-required call `redeploy-old`
(RemoteScriptContractTests.cs:2565, 2584). That label is not the actual redeploy
caller's allow-cold contract. `C912_Cold_runner_verification_uses_mounts_and_writability_not_payloads`
pins allow-cold text in all five host callers, rather than executing every caller's
marker-reading path. New regression coverage must execute the real failure
against a captured marker in a private scratch copy, then model that shape in
the rolling fake without stubbing readiness success.

Initially pending: real receipt/marker capture; exact source predicate diagnosis; committed
named-assertion red on base; script fix; cold/missing/malformed/foreign reader
matrix; checkpoint rows and rolling harness green with exact amended counts.
No build, TUnit, harness, deploy phase or drain-clear operation ran. No Unit lane
was selected. jq 1.7.1 was downloaded to `/tmp/card0973-KPglJm/bin/jq` and its
SHA-256 matched the brief's pinned checksum; it is ready for a resumed test run
with that bin directory prepended to PATH.

Resume on the assigned fast-forward branch after the live evidence is available.
Use direct `scripts/run-checkpoint.ps1` rows for C849/C912 RemoteScriptContractTests,
DockerStackContractTests and affected rolling script contracts; use
`scripts/build-slot.ps1` for the harness. Commit/push tests before diagnostic red
and production changes before final green. The script-only scope does not
authorize a server change or a production rollout.
