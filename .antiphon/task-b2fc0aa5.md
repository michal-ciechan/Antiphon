# CARD-1609 continuation b2fc0aa5

DRAFT: checkpoint run remains in flight. Replace this outcome and the pending table before settlement.

S5 is incomplete. Six receipt witnesses (J2-J6 and J9) were added red-first where the required behavior was missing. J1 plus those six methods comprise the current seven-method receipt class. J7/J8/J10 and N1-N3 remain absent; CP-3 therefore has 22 methods rather than its required 25. No missing method, native row, or production audit is credited.

Assigned base: `a9ed7b7bf1a3d889b50e2df39ee3df0c3eacadae`.
Branch: `feat/card-task-b2fc0aa5`.
Current pushed tip: `7ad5d070fcf52b02acf1ce961037e56d85eea645`.

## Published changes

| Commit | Change |
|---|---|
| `63b4994b81324f306e55301ffb57148123cdc6dc` | J2/J3/J4/J9 receipt witnesses and exact Linux Native roster entries. |
| `89b4c5a494c2801ddfd8dcd6ee26ac519a089556` | Strict canonical receipt, custody/links/writable ancestors, schema/policy, expected digest, freshness and incarnation binding; durable writes. Strengthened flush/inode/fake-clock vectors. |
| `9b2cbd6b67514b20533aef5eeb290b60968b2815` | J5 changed-content/publication and J6 between-delete busy/census witnesses; bounded fake-Docker fault. |
| `8536d74cf310e5f4208f82c24de42ff4d6d27d91` | Temp apply uses the same real read-only scan as preview; compares local inventory; exact state/dind/tmp/work removal with individual durable intents/results; four census attempts inside 180 monotonic seconds and bounded task HTTP reads. |
| `7ad5d070fcf52b02acf1ce961037e56d85eea645` | Reinspect the complete incarnation immediately before each delete; preview holds rollout/cache locks; hash ref names, reflogs, indices and linked-admin metadata; update owner docs and obsolete Compose-deletion pins. |

All commits were pushed individually. No branch switch, amend, reset, rebase, force push, rc edit, tag, deployment or activation occurred.

Changed production files: `scripts/c1008-retirement.sh`, `scripts/c1008-content-audit.sh`, `scripts/c590-remote.sh`. Changed fixture: `scripts/fixtures/c1008-fake-docker.sh`. Changed tests: `VolumeRetirementReceiptTests.cs`, `WindowsUnitSkipRoster.cs`, `WindowsUnitPlatformAdmissionTests.cs`, `RemoteScriptContractTests.cs`, `DockerStackDocumentationTests.cs`. Owner docs: `docs/docker-stack.md`, `docs/orchestration-loop.md`.

Receipt authority stays on the host. Each preview nonce has an immutable `preview-<nonce>.json`; `preview.json` and `proof.md` are current renderings. Ready consumption rejects noncanonical/multiple/duplicate JSON, unknown authorizing members, wrong schema/policy, link/custody/digest failures, expired/future proofs, changed daemon/retirement/incarnations, and changed local safety inventory. Host stat includes nanosecond ctime. All label values participate through a digest; unapproved labels are not newly exported into the v1 journal. File and directory flushes precede authorizing acknowledgements and delete intents. Successful deletions get their own result; Completed no longer invents intents for absent targets.

Temp no longer uses `compose_temp down -v`. It removes exact names, work last, retaining the remainder after a later guard failure. Main keeps its v1 create/start audit and generation/census behavior. The docs/test replacements preserve retired/project-only deletion protection and strengthen the ordering pins to guard -> incarnation -> durable intent -> exact rm -> result. No test was removed, skipped or relaxed. Linux-only Native entries increased from 77 identities/88 results to 83/94; portable H/W methods acquired no Linux gate. Windows C1210 execution is still owed.

## Recorded execution

All builds/tests used host slots, `TEMP=/tmp`, isolated `bin-c1609-j/` or checkpoint-owned `bin-c1609-final/`, and `UseAppHost=false`. No Pty/native desktop suite ran. Extra targeted runs were needed to establish new assertion-red witnesses and repair them before the final closed checkpoint selection.

| Selection / source | Executed | Passed | Failed | Skipped | Outcome |
|---|---:|---:|---:|---:|---|
| Receipt class at `63b4994b8` | 5 | 2 | 3 | 0 | Intended J2 duplicate-key acceptance, J3 name/incarnation acceptance, J4 stale proof acceptance. 1m22s. |
| Receipt class at `89b4c5a49` | 5 | 5 | 0 | 0 | 1m41s. |
| J5/J6 at `9b2cbd6b6` | 2 | 0 | 2 | 0 | Intended: unsafe changes still deleted four volumes; broad Compose removal bypassed between-delete guard. 3m10s. |
| J5/J6 with scripts at `8536d74cf` | 2 | 2 | 0 | 0 | 4m09s. Selected C# bodies unchanged from the preceding build; final source-qualified build below is separate. |

Manual isolated build durations: 10m18s, 9m23s, 11m34s. Final checkpoint driver build reports 12m30s. These exceed the plan's provisional ten-minute build cap in three cases; no test timeout was increased. J5's red run was 2m19s, exceeding the plan's provisional 120-second method budget. Comparable loaded p95 and production p95 remain unknown; preserve these costs for Debug/performance follow-up rather than claiming the planned estimates were measured.

Targeted logs/TRX: `.antiphon/c1609-j-red`, `.antiphon/c1609-j-green`, `.antiphon/c1609-j56-red`, `.antiphon/c1609-j56-green`, with matching `.log` files.

Final checkpoint run: `.antiphon/checkpoints/20261030-124644-de27/`; manifest `.antiphon/c1609-final.yaml`. Import succeeded, 16 rows, only the expected CP-11 duplicate-build warning. Selection: CP-3,4,5,6,7,8,9,15,16. Min counts were not reduced. Row caps follow the plan (10/2/2/6/4/2/6/6/6 minutes), charged cap 52 minutes, wall cap narrowed to 55 minutes for this continuation budget. The tool takes its own build/row leases.

| CP | Executed / passed / failed / skipped | Status |
|---|---|---|
| CP-3 | pending | Requires 25; only 22 present. |
| CP-4 | pending | |
| CP-5 | pending | |
| CP-6 | pending | |
| CP-7 | pending | |
| CP-8 | pending | |
| CP-9 | pending | |
| CP-15 | pending | |
| CP-16 | pending | |
| CP-10 | 0 / 0 / 0 / 0 | Owed: N1-N3 absent. |
| CP-11/12/13 | 0 / 0 / 0 / 0 | Owed: native Windows, unavailable in this Linux checkout. |

FINAL_CHECKPOINT_LINES_PENDING

## Brief's open points and remaining acceptance

(a) **Historical generation checks remain in the decision path, and the plan does not allow them.** `case_retire_temp_containers` still calls `c1008_previous_generation`; `c1008_bind_generation` and historical model/image lookup remain ahead of content proof. W5 still includes the obsolete temp SHA/image mismatch refusal, alongside valid main safeguards. This continuation did not remove those dependencies. D-9 says: "The retired-temp entry must not first fail `TempStackMissing`, source lookup, old Compose parsing or C994 image selection." It also says: "The creating image is a diagnostic field, not required to exist." Next Code must replace the historical admission with supported actual topology/current installed tooling, prove it with a witness, and retain main's mismatch refusal. It must not describe the remaining temp admission as tooling-only provenance.

(b) **Implemented the shell's 180-second gate rather than amending the plan downward.** The monotonic deadline is shared by all four attempts and nested task HTTP retries; each task HTTP read is capped by the remaining budget. Only classified Drift/Unstable retry with 2/5/10 seconds. Successful, terminal and refusal attempts are journaled. Main's v1 behavior is retained. The wrapper's full D-19 contract, including effect-boundary timing/guard-refresh cases, still needs its remaining witness coverage; do not treat the current J6 subset as the whole planned matrix.

(c) **WrongLane is correct.** `C1008_Documentation_and_transport_pins_match` independently protects the host-only retirement entry against a nested-daemon invocation and asserts zero removals. It is not a historical-image equality requirement.

Next Code still owes J7/J8/J10 and N1-N3, full J2/J4/J5/J6 matrix closure, current audit descriptor/tool installation and historical topology decoupling, manual Preview/Apply attestation/transport, durable partial resume/reconciliation, and the remaining planned wrapper/locking/proof/freshness obligations. In particular preview expiry is still checked late, before deletion, rather than solely at apply admission with a separately fresh long audit; temp partial resume is not qualified and can refuse an existing pending intent. N1 must qualify/fix real offline/nocopy/read-only-root and `/tmp` namespace behavior, not merely assert argument strings. No release credit follows the targeted green subset.

The temp publication consumer still has the legacy reader after failed v2 unbundling (`c1609_unbundle_host_proof` / `load_host_origin_proof`). A schema-2 retirement receipt does not itself establish protocol-2 publication semantics. Close that compatibility path with refusal witnesses while preserving the explicitly isolated v1 scanner adapter. This upstream closure is required before enabling deletes.

The first production action remains **preview-only** after reviewed tool activation. Measure all repositories, exact CARD-1605 template matches, unclassified crashed-test repositories including `/tmp`, raw-header refusals, counts/rates and unknown denominators before enabling any apply/delete. No production volume audit or deletion was performed here. The current wrapper's complete v2 Preview/Apply/explicit-proof flow still needs repair; do not mistake its old census-only DryRun return for the required production content preview.

## Static checks and publication

No `.csproj`, `.sln`, `.props`, `.targets`, package/SDK definition, land policy or approval manifest changed. The real command `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1609-j/Antiphon.LandGraphAudit.dll` exited 0: digest `20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6`, count 66, approved. `-Update` was not run. That check's definition inputs remain unchanged at the final tip.

`scripts/check-evidence-diff.ps1` over the full assigned base..tip exited 0: 5 commits, 0 evidence entries, 0 violations. `scripts/check-os-witnesses.ps1` exited 0 with `.antiphon/c1609-os-witnesses.json`: ten inspected paths, twenty unchanged lexical candidates, no changed OS-selected production branch, no AppHost restart required. These are static checks, not native Windows credit.

After `git fetch origin master`, merge-tree against `698bf1b73e8abf449d5569ea74ac8c82e80f92a1` exited 0; tree `ff839b82aa71f13f13be8ef1845b90bfe87ca6f2`. This is a conflict-free simulation, not land or activation.

Input gap: `.antiphon/task-19f9c7ca.md` was absent from this mirror. The available earlier report, full plan, and actual source were used; no desktop files were read. Existing design-review report files named by the plan were not present here. This is not independent final Review.

OUTPUT_CLEANUP_AND_REMOTE_PENDING

## Exact owed commands

On Linux after completing the missing implementation, import the same plan and run through the checkpoint tool:

```text
dotnet run --project tools/Antiphon.Checkpoints -- import --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --rows CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-15,CP-16 --max-wait 45s
dotnet run --project tools/Antiphon.Checkpoints -- wait --run <new-run-id> --max-wait 45s
```

Use a fresh owned tool output and bootstrap through `scripts/build-slot.ps1` if building the tool. Checkpoint builds/rows take their own slots; do not wrap the active checkpoint orchestration in another lease. Set `TEMP=/tmp` and `UseAppHost=false` on Linux. Do not rerun already green same-source rows solely for report wording; use the exact incomplete/affected selection.

Native Windows remains owed, including a local isolated build (CP-11 cannot reuse Linux output):

```text
dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-29-card-1609-content-derived-volume-retirement-plan.md --rows CP-11,CP-12,CP-13 --max-wait 45s
dotnet run --project tools/Antiphon.Checkpoints -- wait --run <windows-run-id> --max-wait 45s
```

CP-11 filter: `/*/Antiphon.Tests.Scripts/(VolumeRetirementHostProofTests*)|(VolumeRetirementWrapperTests*)/*`, expected 12.
CP-12 filter: `/*/Antiphon.Tests.Scripts/WindowsUnitPlatformAdmissionTests/(C1210_Guard_matches_native_platform*)|(C1210_Native_linux_entries_guard_before_work*)|(C1210_Roster_closure_matches_source_gates*)`, expected 3.
CP-13 filter: `/*/Antiphon.Tests.Infrastructure/DockerStackDocumentationTests/*`, expected 16.

The later production preview command shape, **only after its complete v2 wrapper and installed-tool identity are reviewed and activated**, is `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-tool-sha> -Phase retire-temp -DryRun -ProjectId <real-project-id>`. Confirm this actually reaches the v2 content preview; retain JSONL/proof.md and the prevalence measurements above. No Apply command is authorized by this report. Reviewed master/rc landing and rc activation are separate caller steps, after top-up 38 and before CARD-1615.

--- next stage ---
next: code
handoff: Complete the missing CARD-1609 witnesses and implementation obligations, repair/execute exact incomplete checkpoint selections with preserved timing/source evidence, then commission final Review. Do not enable production deletes from this partial continuation.
artifact: /work/worktrees/task-b2fc0aa5/.antiphon/task-b2fc0aa5.md
