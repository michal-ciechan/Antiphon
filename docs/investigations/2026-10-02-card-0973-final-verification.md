Fixed CARD-0973: cold cache validation survives normal retirement of the seed-time image; all final script checks passed.

Source: f8e15bbb2240cecea475c2cf2732ceec6e8edbf2, pushed on feat/card-task-6c0b3739, fast-forward from the assigned base. No deploy phase, runner restart, additional host probe or drain clear ran. No server code changed. The supplied live evidence resolved the initial SSH-access blocker.

Diagnosis: live evidence in .antiphon/inbox/EVIDENCE-0973.md confirms deployed/base c590-remote.sh:2030-2031 rejected the marker because deploy-parent itself deleted image sha256:09fe5601ba1d88a8658e7d78d1818a16b15c1cf1d39cc859601fcdaedc364bc1. All format predicates passed. The eight-field marker is captured exactly in scripts/fixtures/c973-cold-seed-accepted.txt.

Changes:
- scripts/c590-remote.sh: cold image identity is provenance checked by format; readiness still checks marker file shape, exact keys/volume names, source SHA format, canonical Docker root, volume identity, labels and ownership. Full-marker recovery/hash rules remain unchanged.
- c849_image: when neither the requested deployment tag nor historical cold image remains, use the existing identified standing runner's locally inspectable image; malformed cold image identity and foreign/missing main/image remain refusals.
- Retired cold verification: record a currently available resolved helper in rollback.txt; full Retired still requires the historical seed/recovery image.
- scripts/test-deploy-server2.ps1 and fixtures/c727-fake-verify.ps1: every Run-C727 carries the real cold marker. Parent check succeeds while its seed image exists; fake deployment then removes that image before real marker verification. T-20 also refuses missing/malformed/foreign markers without clearing the drain. No full marker is prewritten for redeploy.
- fixtures/c973-marker-reader.sh: execute production marker-reading slices with private Docker/privilege/status boundary fakes. Verification and Retired execute their whole host-case bodies. Parent/temp/retire deployment cases execute only the actual prepare/ready reader slice, never deploy. SavedDonor transport preserves its prior seam; full-required validation is tested separately.
- RemoteScriptContractTests: six separately reported allow-cold readers, five separately reported helper contexts, and one full-required control; reject missing/malformed/duplicate/symlink/foreign markers, bad source/image fields, foreign roots/volumes, and missing/foreign helper candidates. Corrected the old misleading redeploy-old full-required label.
- .gitattributes: pin new Linux shell and captured marker to LF; fake supports WSL path mapping on Windows. Windows execution remains unqualified in this Linux task.
- docs/docker-stack.md documents cold provenance/helper and Retired receipt semantics.

Red-first evidence:
- CP-973-rolling-red at b119040aa022e35fe865872931675266e40b63e1: exit 1 at named T-20 pruned cold seed image verifies before admission, showing DIAGNOSIS=CacheSeedMarkerInvalid and HostCaseFailed verify-runner-caches exit=2. 1 invocation/1 failed assertion before any group completed.
- CP-973-red at 753bbd12992e041a2ff4eaae84aee4827b6aaf9f: 12 executed, 0 passed, 12 intended assertion failures, 0 skipped. Production remained identical to the deployed/base script. Six pruned-seed-image-accepted, five available-main-helper, one full-context-refused-c849_seed assertion failed.
- Diagnostic attempts that are not red evidence: first red build failed on an assertion overload; corrected and pushed. One red input was refused for a short ExpectedSourceSha. Red row reruns=2. First final cache-row input had a mistyped full SHA and was refused before build/slot; corrected using git rev-parse HEAD. Final cache-row reruns=1. No hidden builds or broad Unit lane.

Final verification:
- CP-973-caches: 39/39 passed, 0 failed/skipped; one isolated build.
- CP-18: 215/215 passed, 0 failed/skipped; reused the same verified build; all five affected rolling contract classes, including DockerStackContractTests and the entire RemoteScriptContractTests class.
- CP-21: 10/10 passed, 0 failed/skipped; reused that build.
- CP-20: 20 groups / 59 driver invocations / 206 assertions / 0 failures through build-slot.ps1, slot granted, wait 0s. Prior roster 19/55/197 plus T-20 1/4/9. Wrapper held its lease for 319s and released it.
- All three final TUnit source.json receipts validated with validate-checkpoint-receipt.ps1 against f8e15bbb2240cecea475c2cf2732ceec6e8edbf2; clean source, buildSource=verified, dirty=0.
- git diff --check and bash -n for both shell files passed; receipt validation and shell syntax checks are read-only static qualification, not extra build/test rows.
- jq 1.7.1 checksum verified: 5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5. Binary: /tmp/card0973-KPglJm/bin/jq.
- Final detached scratch worktree /tmp/card0973-KPglJm/landing-final: git -c rebase.autoStash=false -c rebase.updateRefs=false rebase origin/master returned exit 0, HEAD is up to date; origin/master=4453dab63e5005414b2ded095b674f7b0ef1919e. Assigned branch was not rebased/reset/amended.

Scope/remaining acceptance: No Unit lane per brief. Backend/runtime rolling-plan rows are outside this script fix; live CP-22..24 were prohibited. Affected rolling script rows CP-18/20/21 ran. Review/land precede any trusted-desktop production retry; current standing drain was not cleared. PCs stay pending method-scoped SourceLanding Mutation. No production success is claimed.

Rerun closed rows using docs/superpowers/plans/2026-10-02-card-0973-cold-marker-verification-fix.md, PATH=/tmp/card0973-KPglJm/bin:$PATH, C804_ORPHAN_SWEEP_ROOT=c973-disabled, TUNIT_MAX_PARALLEL_TESTS=1. run-checkpoint.ps1 takes its own slot; only the rolling harness uses the build-slot wrapper.

Exact emitted checkpoint lines (preserved unedited):

c973-red.log
CHECKPOINT CP-973-red commit=b119040aa022e35fe865872931675266e40b63e1 build=failed filter=/*/*/RemoteScriptContractTests*/C973_* executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-6c0b3739/.antiphon/c973-checkpoints-red/CP-973-red-20261002-131820-ccf1/run.trx slot=granted waited=0s dirty=0 source=b119040aa022e35fe865872931675266e40b63e1 sourceState=clean buildSource=unknown

c973-red-rerun.log
CHECKPOINT CP-973-red commit=unknown build=n/a filter=/*/*/RemoteScriptContractTests*/C973_* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown

c973-red-rerun2.log
CHECKPOINT CP-973-red commit=753bbd12992e041a2ff4eaae84aee4827b6aaf9f build=ok filter=/*/*/RemoteScriptContractTests*/C973_* executed=12 passed=0 failed=12 skipped=0 trx=/work/worktrees/task-6c0b3739/.antiphon/c973-checkpoints-red/CP-973-red-20261002-132110-0146/run.trx slot=granted waited=0s dirty=0 source=753bbd12992e041a2ff4eaae84aee4827b6aaf9f sourceState=clean buildSource=verified

c973-caches.log
CHECKPOINT CP-973-caches commit=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 build=n/a filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=0 source=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 sourceState=unknown buildSource=unknown reason=source_mismatch

c973-caches-rerun.log
CHECKPOINT CP-973-caches commit=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*) executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-6c0b3739/.antiphon/c973-checkpoints-final/CP-973-caches-20261002-132558-d443/run.trx slot=granted waited=0s dirty=0 source=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 sourceState=clean buildSource=verified

c973-contracts.log
CHECKPOINT CP-18 commit=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 build=reused filter=/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)/* executed=215 passed=215 failed=0 skipped=0 trx=/work/worktrees/task-6c0b3739/.antiphon/c973-checkpoints-final/CP-18-20261002-133122-dc8a/run.trx slot=granted waited=0s dirty=0 source=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 sourceState=clean buildSource=verified

c973-docs.log
CHECKPOINT CP-21 commit=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 build=reused filter=/*/*/DockerStackDocumentationTests*/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-6c0b3739/.antiphon/c973-checkpoints-final/CP-21-20261002-133327-1d13/run.trx slot=granted waited=0s dirty=0 source=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 sourceState=clean buildSource=verified

Non-TUnit row: CHECKPOINT CP-20 commit=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 build=n/a filter=pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-rolling -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1 executed=0 passed=0 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=f8e15bbb2240cecea475c2cf2732ceec6e8edbf2 sourceState=clean buildSource=notApplicable groups=20 invocations=59 assertions=206 failures=0

Final source marker-reference index (file scripts/c590-remote.sh, exact final line numbers; includes declarations/writes as well as readers):
1300 build_server2_images() C849_READY="$SERVER2_ROOT/cache/seed-accepted"
1321 c849_image()                 if [ -f "$C849_READY" ] && grep -Fxq kind=cold "$C849_READY"; then
1322 c849_image()                     marker_image="$(sed -n 's/^image=//p' "$C849_READY")"
1335 c849_image()                     marker_image="$(sed -n 's/^image=//p' "$C849_READY" 2>/dev/null | head -n 1)"
1393 c849_volume()     if [ "$fresh" = 0 ] && [ "$create" = yes ] && [ ! -s "$C849_READY" ]; then
1820 c849_cold_seed()     [ ! -e "$C849_READY" ] && [ ! -L "$C849_READY" ] \
1850 c849_cold_seed()     [ ! -e "$C849_READY" ] && [ ! -L "$C849_READY" ] || write_result false CacheSeedMarkerInvalid 2
1853 c849_cold_seed()         > "$C849_READY.tmp-$RUN"
1854 c849_cold_seed()     chmod 0600 "$C849_READY.tmp-$RUN"
1856 c849_cold_seed()     mv -T -- "$C849_READY.tmp-$RUN" "$C849_READY" || write_result false CacheSeedMarkerInvalid 2
1865 c849_seed()         [ ! -e "$C849_READY" ] && [ ! -L "$C849_READY" ] \
1872 c849_seed()     if [ -f "$C849_READY" ] && grep -Fxq kind=cold "$C849_READY"; then
1878 c849_seed()     if [ -e "$C849_READY" ] || [ -L "$C849_READY" ]; then
1880 c849_seed()             c849_require_ready
1882 c849_seed()             c849_require_ready allow-cold
2004 c849_seed()         "${donor:-saved}" "$donor_image" "$now" "$payload_hash" "$reference_hash" "$package_bytes" "$npm_bytes" "$recovery" > "$C849_READY.tmp-$RUN"
2005 c849_seed()     mv "$C849_READY.tmp-$RUN" "$C849_READY"
2011 c849_require_ready() c849_require_ready() {
2014 c849_require_ready()     [ -e "$C849_READY" ] || [ -L "$C849_READY" ] \
2016 c849_require_ready()     [ -f "$C849_READY" ] && [ ! -L "$C849_READY" ] && [ -s "$C849_READY" ] \
2018 c849_require_ready()     if grep -Eq '^(schema|kind|cold)=' "$C849_READY"; then
2019 c849_require_ready()         [ "$(wc -l < "$C849_READY")" -eq 8 ] \
2020 c849_require_ready()             && [ "$(cut -d= -f1 "$C849_READY" | sort -u | wc -l)" -eq 8 ] \
2021 c849_require_ready()             && grep -Fxq schema=2 "$C849_READY" \
2022 c849_require_ready()             && grep -Fxq kind=cold "$C849_READY" \
2023 c849_require_ready()             && grep -Fxq cold=true "$C849_READY" \
2024 c849_require_ready()             && grep -Eq '^source-sha=[0-9a-f]{40}$' "$C849_READY" \
2025 c849_require_ready()             && grep -Fxq "packages=$C849_PACKAGES" "$C849_READY" \
2026 c849_require_ready()             && grep -Fxq "scratch=$C849_SCRATCH" "$C849_READY" \
2027 c849_require_ready()             && grep -Fxq "npm=$C849_NPM" "$C849_READY" \
2029 c849_require_ready()         image="$(sed -n 's/^image=//p' "$C849_READY")"
2049 c849_require_ready()     recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
2054 c849_require_ready()     expected="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
2129 c849_preview()     if [ -s "$C849_READY" ]; then
2130 c849_preview()         recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
2275 c849_reset()     if [ -e "$C849_READY" ] || [ -L "$C849_READY" ]; then
2276 c849_reset()         if [ -L "$C849_READY" ] || { [ -f "$C849_READY" ] && grep -Eq '^(schema|kind|cold)=' "$C849_READY"; }; then
2277 c849_reset()             c849_require_ready
2333 c849_prune()     c849_require_ready
2354 c849_prune()     c849_require_ready
2357 c849_prune()     recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
2361 c849_prune()     expected_hash="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
2364 c849_prune()     donor_image="$(sed -n 's/^image=//p' "$C849_READY" | head -n 1)"
2570 c849_fixture_prepare()     C849_READY="$SERVER2_ROOT/cache/fixture-seed-accepted"
2587 c849_fixture_prepare()     printf 'accepted\n' > "$C849_READY"
2638 c849_fixture_seed()     C849_READY="$SERVER2_ROOT/cache/fixture-seed-ready"
2692 c849_fixture_seed()     [ -s "$CASE_DIR/.fixture-seed-smoke" ] && [ -s "$C849_READY" ] \
2694 c849_fixture_seed()     recovery="$(sed -n 's/^recovery=//p' "$C849_READY" | head -n 1)"
2700 c849_fixture_seed()     after="$(sed -n 's/^payload-sha256=//p' "$C849_READY" | head -n 1)"
2731 c849_fixture_unmarked_consumer()     C849_READY="$SERVER2_ROOT/cache/unmarked-ready"
3168 c849_fixture_prune()     C849_READY="$SERVER2_ROOT/cache/fixture-prune-ready"
3177 c849_fixture_prune()     printf 'image=%s\npayload-sha256=%s\nrecovery=%s\n' "$image_id" "$payload" "$recovery" > "$C849_READY"
3295 case_verify_runner_caches()     c849_require_ready allow-cold
3307 case_verify_runner_caches()     if [ "$C849_KIND" = full ]; then sed -n 's/^payload-sha256=//p' "$C849_READY" > "$CASE_DIR/seed-hash.txt"; fi
3359 case_verify_runner_caches_retired()     c849_require_ready allow-cold
3366 case_verify_runner_caches_retired()         rollback_image="$(sed -n 's/^image=//p' "$C849_READY" | head -n 1)"
3379 case_verify_runner_caches_retired()     if [ "$C849_KIND" = full ]; then sed -n 's/^payload-sha256=//p' "$C849_READY" > "$CASE_DIR/seed-hash.txt"; fi
3429 case_deploy_parent()     c849_require_ready allow-cold
3585 case_deploy_temp_runner()     c849_require_ready allow-cold
3695 case_retire_temp_runner()     c849_require_ready allow-cold

Driver audit: scripts/deploy-server2.ps1 has no direct marker readers. Ordinary Seed 162; temp deployment 163; temp verification 168; parent deployment 204; standing verification 209; retirement 246; SavedDonor transported only for Seed at 123. drain-old 178 and drain-temp 219 only read runner status; admission clear at 213 follows verification.

Committed base/source reader audit and detailed maintenance exclusions: docs/investigations/2026-10-02-card-0973-marker-reader-audit.md.
