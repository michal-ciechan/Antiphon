# CARD-1954 plan: separate retained acceptance evidence from disposable fixture repos

Card: CARD-1954 `ae39008c-d6cb-494a-8724-0fcab3529290`, "Separate retained acceptance evidence from
disposable fixture repos and rendezvous roots". Backlog, Normal/Soon, rank 8, no history, not parked,
not operator-only, not done. Filed by CARD-1953 (task 68f81b18) from a read-only source audit.

Master read: `870a93d21f2b18c85a8b0905ab689b28e3099813` (`origin/master` on 2026-11-17, equal to the
task base). This is a plan only. No code, setting, host, volume, registry or deployment was touched.

## 1. Problem restated, with evidence

### What was observed

| # | Observation | Source | Cause |
|---|---|---|---|
| O-1 | Gate 9 (`retire-temp`) refused twice on 2026-11-15 with `RecycleGitAuditUnknown audit check=ls-remote status=128 repo=worktrees/task-e479506f/.antiphon/evidence/card-1901/fixtures/b4b4e351.../origin.git reason=inventory`. The two inventory outputs are byte-identical. Neither operation reached Ready. | `origin/feat/card-task-7a422522` `docs/investigations/2026-11-15-retire-temp-refusal-task-e479506f-fixtures.md` (commit `982464a081e23fda47fe61d409d6a2dde211dec9`, not landed) | `VolumeRetirementPublicationFixture.Create(preview:false)` from the CARD-1901 branch wrote bare repos, private-remote repos and reflog-only commits under the checkout's `.antiphon/evidence/card-1901/fixtures/<guid>` and never deleted them. That investigation lists five blockers behind the first one, B1 to B5: no-origin bare repos, `unapproved-origin`, `no-remote-unclassified`, 311 `evidence-name` files and a fixture reflog that would refuse `RecycleUnpublishedWork`. |
| O-2 | The recommended path was (a′): a one-off operator-authorised manual `rm -rf` of the fixture subtree inside the retired volume, then a fresh gate 9. No supported cleanup path exists for a retired temp volume. | same investigation, section 4 | Process gap: the audit correctly refuses, and only a manual write can clear a producer leak. |
| O-3 | 2026-11-13: gate 9 refused on selector fixtures under `.antiphon/selector-evidence/f1903e95/fixtures`. | `docs/investigations/2026-11-13-why-missed-selector-fixture-retirement.md` | CARD-1820 producer. Fixed at the producer (system temp), plus a narrow audit classifier `selector-evidence-v1` (`scripts/c590-remote.sh:7180`, `docs/docker-stack.md:444`). |
| O-4 | The 2026-11-16 refusals (`proof-stale` from the 264-character desktop path, the fence at 11 s, and the 5 s control window) were not caused by fixtures or evidence. | `docs/investigations/2026-11-16-retire-temp-live-binding-refusal.md`, `-fence-11s.md`, `origin/feat/card-task-d8f79eb5:.../2026-11-16-retire-temp-proof-stale.md` (lines 187-226: "Not an observed unpublished-canary refusal") | Timing and path issues, now fixed by CARD-1955/1956. |
| O-5 | "The operator twice had to use the audited manual removal fallback." | brief | **UNOBSERVED** in the repository. One recommendation (O-2) is recorded, for a single volume generation. A second manual removal is not recorded in any committed or branch investigation found (searched the newest 400 origin branches). |
| O-6 | Any of the five CARD-1954 families caused a recorded refusal. | grep of `containment-fixtures`, `acceptance/card-0467`, `card-0418/f5`, `acceptance/card-0419`, `queue-race-` over `docs/` and `scripts/` | **UNOBSERVED.** No refusal names these paths. CARD-1954 is a preventive card for the same defect class as O-1 and O-3. |

### How the audit treats these locations (unchanged by this plan)

- Temp V2 inventory: a common Git directory without `origin` refuses `ls-remote status=128`. After the proof step, `c1609_prepare_common` refuses `no-remote-unclassified` (`scripts/c1008-content-audit.sh:296`) unless it is the exact CARD-1605 seed (`:105`) or a `selector-evidence-v1` fixture.
- `c1609_scan_evidence` (`scripts/c1008-content-audit.sh:485-497`) blocks unconditionally on any regular file named `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `REBASE_HEAD`, `BISECT_HEAD` or `MERGE_HEAD` outside a `.git` directory, anywhere in `/work` or the temp runner's `/tmp`, even when the file is ignored. Every bare repository's `HEAD` hits this rule.
- `c1609_scan_git` (`:370`) lists every `.git` under both volumes. Published, dirty and unpublished state map to `RecycleUnpublishedWork` (exit 3) or `RecycleWorktreeDirty` (exit 4) through `scripts/c1008-retirement.sh:437-446` and `scripts/c590-remote.sh:9050-9079`.
- Ignored plain files inside an index-bearing checkout are not protected work and are not inspected (`docs/docker-stack.md:516`). An ignored `.antiphon/acceptance/<card>/<guid>/` that holds only regular files with no Git-shaped names therefore passes.
- `/var/tmp` inside the runner container is the container overlay, not a volume. `/tmp` is the `runner-tmp` volume and is audited. `/work` is the work volume. (Measured by CARD-1953 and CARD-1901 with `df -T`. The dockerd that runs in the runner container shares that filesystem: `docker-compose.server2-runner.yml:109-113` mounts `dind-data:/var/lib/docker` in the same service.)

### Ground truth: what the card assumes vs what the code does

| Card / brief assumption | Code at `870a93d21` | Consequence for the plan |
|---|---|---|
| The five families "avoid a future retired-runner audit refusal" | Only one of the five can put **Git state** into a Linux runner volume: `ChannelOutboundIsolatedTests.Composed.cs:45-79`. It holds two no-remote repos, `repo` and `repo-q`. It is Linux-only (`:38`) and gated behind three opt-ins (`ChannelOutboundIsolatedTests.cs:132-137`). | This is the only family that is retirement-relevant. Whether it has ever run on server2 is **UNOBSERVED**. |
| `OwnedProcessContainmentFixture` retains fixture state | `.antiphon/containment-fixtures/<guid>` (`TestHelpers/OwnedProcessContainmentFixture.cs:12-13`) holds `fixture.sh`, `*.pid`, `spawn` and `release`. These are plain files, with no Git. `Dispose` keeps them deliberately (`:115`). It is used only by `LinuxOwnedGitProcessTests` (`:98`, `:129`). | Hygiene (disk) only. The audit passes ignored plain files. |
| `LandQueueRaceWorker` leaves rendezvous files under the checkout | The root is relative to the **process cwd**, `Path.Combine(".antiphon","acceptance","card-0467","queue-race-<guid>")` (`TestHelpers/LandQueueRaceWorker.cs:48`). The TUnit cwd is the test output directory, so this lands under the ignored `tests/Antiphon.Tests/bin/...`, not the checkout's `.antiphon`. The child refuses any root outside that cwd prefix or without `owner` (`:21-23`). There is no Git. | Hygiene only. The ownership check must move with the root and stay fail-closed. `TestDbFixtureLazyInitializationTests.A_failing_worker_exits_1_without_running_tests` (`:140-162`) pins the refusal. |
| `LandDeliveryFixture` keeps real repos under the checkout | It does: `repo`, bare `remote.git`, linked `trees/source`, and `-second` variants for a borrowed fixture (`Fixtures/LandDeliveryFixture.cs:28,41-44,67-81`). `DisposeAsync` keeps everything (`:446`). **But** `InitializeAsync` requires native Windows (`:26,59`). The Linux-runnable `LandDeliveryFixtureProbeTests` (14 tests) construct the fixture without `InitializeAsync` and write only plain files to `Root`. The child owner check pins the checkout prefix (`Fixtures/OwnedLandServerChild.cs:14`). | The Git-bearing path runs only on the Windows desktop, where no volume retirement exists, so it is not retirement-relevant. On Linux it is a plain-file leak. |
| `OutputDistillationApplyCanaryTests` keeps its repo with acceptance evidence | It does: `.antiphon/acceptance/card-0419/<guid>/repo` with evidence beside it (`OutputDistillationApplyCanaryTests.cs:51-56,199-202`). It is `NativeWindowsGate.Require` (`:43`), a live-provider canary behind `ANTIPHON_DISTILLER_APPLY_CANARY` and an approval (`Fixtures/DistillerCanaryOptions.cs:11-13`). The owner check pins the checkout prefix (`:15`). The report store must resolve `Repo/.antiphon/reports` (`:245`). | Windows-only, so not retirement-relevant. Its repo must stay on a **persistent** root (see the next row). |
| "Keep report-store persistent-root constraints intact" | `AgentReportStore.IsPersistent` refuses any root under `Path.GetTempPath()` (`server/Infrastructure/Files/AgentReportStore.cs:261-264`). `ReportWorkspace.PersistentOutputParent` already falls back to `~/.antiphon/test-output/card-0419` when the checkout is under temp (`tests/Antiphon.Tests/TestHelpers/ReportWorkspace.cs:41-56`). On Linux, `/var/tmp` is not `GetTempPath()` (`/tmp`). On Windows, `%TEMP%` is. | A family whose repo acts as the main report checkout cannot use `%TEMP%` on Windows. This plan uses a persistent non-temp base for those (D-4). |
| The fix belongs in the audit (marker / root / naming rule) | The audit already fails closed. Every repo in `/work` or `/tmp` is work unless it matches one of three exact byte or shape classifiers. The 2026-11-15 investigation (section 4d) recommends against a new path classifier. | The plan does **not** change the audit (D-1). |
| Card: the producers named are the complete set | CARD-1953 audited 779 `.antiphon` references in `tests/**/*.cs`. `ReportWorkspace` (Linux Unit, real Git under the checkout's `.antiphon/test-output/card-0419`) deletes its root in `DisposeAsync`, but a killed test host leaves it. `MarkdownPdfRealBrowserTests.cs:35` writes `.antiphon/test-output/card-0418/v20-r7`; whether it holds Git is not inspected. | Out of scope (R-5). Each is named as a residual risk. |

## 2. Design

### Decisions

- **D-1: the audit stays unchanged; separation is by physical root.** Disposable fixture repositories, rendezvous files and other operational state live in a unique owned root **outside every audited runner volume**. Retained acceptance evidence stays at its existing checkout evidence location as **plain regular files only**. The audit gains no marker, no path rule and no classifier. Fail-closed holds by construction: an unmarked path, and also a marked one, still counts as work when it is a repository in `/work` or `/tmp`. Nothing is exempted.
  - Rejected: an owner-marker classifier (`.antiphon-disposable-fixture`) in `c1008-content-audit.sh`. Any agent can write a marker, so it would open an exemption for real unpublished commits. It would change pinned retirement tooling (re-export, fresh preview) and overlap the retirement scripts CARD-1957 is changing.
  - Rejected: a naming rule for `.antiphon/acceptance/**` or `.antiphon/evidence/**/fixtures/**`. These fixtures are oracle shapes for "unpublished work must refuse" (O-1 B5). A path exemption would pass real work written under that name. The 2026-11-15 investigation section 4d gives the same verdict.
  - Rejected: blanket recursive cleanup of `.antiphon/acceptance` at the end of a run. The card forbids it, and it destroys evidence other runs own.
- **D-2: Linux operational base is `/var/tmp`.** Not `/tmp` (`Path.GetTempPath()`), which is the audited `runner-tmp` volume, and not anything under `/work`. This matches CARD-1901 (`VolumeRetirementPublicationFixture.cs:95-96`) and CARD-1953 (`Scripts/ComposeFixtureRoot.cs:12-13`).
- **D-3: Windows operational base is `Path.GetTempPath()`** for families with no persistent-root need: containment, queue race and Composed (Composed is Linux-only and never reaches this branch).
- **D-4: a persistent-disposable base for report-store families.** LandDelivery and C419 use `/var/tmp` on Linux and `%LOCALAPPDATA%\Antiphon\fixture-roots` on Windows. The helper refuses when that base resolves under `Path.GetTempPath()`, mirroring `ReportWorkspace.PersistentOutputParent` and `AgentReportStore.IsPersistent`. Rejected: the user-profile `.antiphon` used by `ReportWorkspace`. It is a less clearly owned location, and LOCALAPPDATA is not roamed.
- **D-5: one shared helper,** `tests/Shared/OwnedFixtureRoot.cs`, linked into `Antiphon.Tests` and `Antiphon.E2E` with **literal** `<Compile Include="..\Shared\OwnedFixtureRoot.cs" Link=... />` items. No wildcard: a glob breaks the checkpoint coverage verb. It provides:
  - `Create(family, kind)`: a unique `antiphon-fixture-<family>-<guid32>` directly under the chosen base, refusing an existing path. It writes an owner marker `.antiphon-fixture-owner` holding the token. The marker serves **only** the fixture's own deletion; the audit never reads it.
  - `Owns(path)`: the check child processes use. The immediate parent is the base, the name matches `antiphon-fixture-<family>-*`, there is no reparse point, and the marker is present and matches. It replaces the three checkout-prefix checks (`LandQueueRaceWorker.cs:21-23`, `OwnedLandServerChild.cs:14`, `DistillerCanaryOptions.cs:15`) with a check that is stricter, not looser.
  - `Dispose`/`DisposeAsync`: before deleting, it checks the exact parent, the exact name, no reparse point on the root or marker, and the marker token. On Windows it clears read-only attributes on Git object files without following linked directories. If the marker was never written, it may delete only an empty directory. It is idempotent. Its phase is `TestTiming.BeginPhase(TestPhaseKind.FixtureTeardown, "fixture-root-delete")`.
  - `RetainEvidence(sourceFile, evidenceRelativeName)`: copies one regular file into the family's evidence root under the checkout (D-6). It **refuses** any relative path that has a segment named `.git` or one of the six evidence-name pseudoref names, a symlink or reparse source, or a hard-linked source. It is the only writer of retained evidence, so evidence cannot become an `evidence-name` blocker.
  - Not adopted: refactoring `ComposeFixtureRoot` or `VolumeRetirementPublicationFixture` onto the helper. They are correct and recently landed; churning them is a follow-up (R-6).
- **D-6: evidence destinations are unchanged.** `<checkout>/.antiphon/acceptance/card-0467/<guid>/` (LandDelivery), `<checkout>/.antiphon/acceptance/card-0419/<guid>/` (C419), `<checkout>/.antiphon/test-output/card-0418/f5/<guid>/` (Composed). Containment and queue race keep **no** retained evidence on success. Their rendezvous and pid files have no acceptance value. On failure, the queue race copies its worker stdout/stderr logs, which today are its only diagnostic (`LandQueueRaceWorker.cs:84-87`), to `<cwd>/.antiphon/acceptance/card-0467/queue-race-<guid>/`. That keeps the current diagnostic location, under the ignored test output, and holds only plain files. The card's "retain only required evidence outside audited runner volumes" (C419) is moot, because C419 is Windows-only. Plain-file evidence inside the ignored checkout `.antiphon` passes the audit (`docs/docker-stack.md:516`).
- **D-7: what is evidence.** For each family, Code copies exactly the files the test already writes as evidence or reads back as receipts (lists in S2 to S4), and never a directory that contains a `.git`, a bare repository or a worktree. The copy happens in `finally`, before operational disposal, on success, assertion failure, setup failure and cancellation.
- **D-8: migration of existing roots: none automated.** Old roots in existing checkouts and volumes are left alone. This card authorises no live host, process or volume cleanup. New runs write the new layout. Old task worktrees age out through runner worktree retention (CARD-1677) or manual operator work. A leftover Git-bearing root in a temp volume still refuses at gate 9, and that refusal is the intended stop-and-report.

### Must NOT change

- The audit's refusal semantics for real work: `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, `RecycleGitAuditUnknown`, `no-remote-unclassified`, `evidence-name`, `hook`, the CARD-1605 seed classifier, `selector-evidence-v1` and `bash-edit-diff-v1`.
- The retirement constants: the 5000 ms control budget, the 900 s proof lifetime, the 11000 bound and the 10 s control read (`read -t 10`). Also custody (`c1609_persist`, the proof-set digest, the trusted-origins registry) and the phase/exchange nonce protocol.
- Every file under `scripts/` that relates to retirement or rollout. In particular: `scripts/c1008-retirement.sh`, `scripts/c1008-content-audit.sh`, `scripts/c590-remote.sh`, `scripts/c1609-host-proof.ps1`, `scripts/c590-real.ps1`, `scripts/deploy-server2.ps1`, `scripts/retire-runner-volumes.ps1`, the `docker-compose*.yml` files, `docs/docker-stack.md`, and `retirement-origins.json` on any host.
- `server/**` and `src/**`: no production change. `AgentReportStore` persistence rules stay as they are.
- Existing functional assertions, timeouts and deadlines in the touched tests. These include the 25-minute Composed deadline, the 90/100 s queue-race budgets and the 10 s containment readiness timeout.
- The `NativeWindowsGate.Require` calls (pinned by `NativeWindowsFixtureGateTests.cs:25,53`). The `UntilAsync`/`UntilProtocolAsync` placement in `LandDeliveryFixture.cs` (pinned by `LandDeliveryWaitRoutingTests`).
- `ComposeFixtureRoot.cs` and `VolumeRetirementPublicationFixture.cs` (D-5).

## 3. Slices

Order: **S1 → (S2 ∥ S3) → S4 → S5.** S1 owns every `.csproj` edit and the shared helper, so later slices never touch a project file. S2 and S3 touch disjoint files and can run in parallel. S4 follows S3 so that it reuses the persistent-disposable path S3 proves first on Windows. S5 is docs only.

Shared files with in-flight work:
- CARD-1957 touches `scripts/c1609-host-proof.ps1`, `scripts/c590-real.ps1` and `docs/docker-stack.md`. A docs task touches `docs/docker-stack.md`. CARD-1893 activation may touch the compose files and `scripts/deploy-server2.ps1`. **This plan touches none of them** (must-not-touch above), so ordering against those cards is free.
- `tests/derived-impact-index.json` is regenerated by most test-adding tasks. Each slice regenerates it last, on its own tip, as the final commit. A merge-tree conflict on it at land is resolved by regenerating on the batch tree, never by hand (see the CARD-1953 and CARD-1893 practice in recent master commits).
- `docs/testing-and-build.md` is broadly shared. S5 adds one new subsection only, appended at the end of "Performance standards" and before "Combined class filters", to keep its hunk isolated.

### S1: shared helper and the two Antiphon.Tests families (Linux lane, server2)

Files:
- New `tests/Shared/OwnedFixtureRoot.cs` (D-5).
- `tests/Antiphon.Tests/Antiphon.Tests.csproj` and `tests/Antiphon.E2E/Antiphon.E2E.csproj`: one literal `Compile Include` each.
- `tests/Antiphon.Tests/TestHelpers/OwnedProcessContainmentFixture.cs`: `DirectoryPath` comes from `OwnedFixtureRoot.Create("containment", Temp)`. `Dispose` adds root deletion **after** `_safety.KillAll()` and the stranger joins, inside the existing teardown phase. The `:115` comment is removed.
- `tests/Antiphon.Tests/TestHelpers/LandQueueRaceWorker.cs`: `RunPairAsync` takes an owned root. `finally` copies the logs only on failure (D-6) and then disposes. `RunAsync` replaces its cwd-prefix check with `OwnedFixtureRoot.Owns(root, "c467-queue-race")` and keeps the `owner` file requirement.
- `tests/Antiphon.Tests/Application/AgentTaskLandNotificationRecoveryTests.cs`: `C467_V09_KeyedQueueRacesAndDistinctEvents` gains a before/after census of `<cwd>/.antiphon/acceptance/card-0467/queue-race-*`, asserting that the difference is empty (witness W-2).
- New `tests/Antiphon.Tests/TestHelpers/C1954FixtureRootTests.cs`, plus test-classification registration if `TestClassificationPolicyTests` requires it.
- `tests/derived-impact-index.json` (regenerated, last commit).

Witnesses:
- **W-1 (red at base):** `C1954FixtureRootTests.Containment_fixture_root_is_outside_checkout_and_removed`. It constructs `new OwnedProcessContainmentFixture()` (existing surface, so it compiles at base) and asserts that `DirectoryPath` is not under the checkout, that on Linux it is directly under `/var/tmp`, and that after `Dispose` the directory is absent. Red at base: the path is under `<checkout>/.antiphon/containment-fixtures`. If `DescendantLeakScope` is Linux-only, the method follows the existing `NativeLinuxGate` and roster pattern rather than adding `[NativeLinuxOnly]`, which would need a policy reseal (see the memory of the NativeLinux policy recipe). Code decides and reports which.
- **W-2 (red at base):** the queue-race census inside `C467_V09`. Red at base: a new `queue-race-*` directory appears under the cwd.
- **W-3 (red at base):** the existing `TestDbFixtureLazyInitializationTests.A_failing_worker_exits_1_without_running_tests` must stay green. Its unowned root, under `GetTempPath()` with no marker, is still refused. As the S1 positive control, Code shows that the new `Owns` refuses that path.
- Helper design tests, green-only, new API: `Create_refuses_existing_path`, `Dispose_refuses_replaced_root_or_marker` (marker mismatch or a reparse root leaves the root in place and throws), `Dispose_after_setup_failure_removes_only_empty_root`, `Persistent_kind_refuses_temp_base` (Windows: refuses under `GetTempPath`; Linux: `/var/tmp` accepted), and `RetainEvidence_refuses_git_shaped_names` (an `[Arguments]` row for each of `.git`, `HEAD`, `ORIG_HEAD`, `FETCH_HEAD`, `REBASE_HEAD`, `BISECT_HEAD`, `MERGE_HEAD`, a nested `x/.git/y`, and a symlink source). Mutation is waived (operator 2026-11-12); these are not counted as red witnesses.

### S2: Composed F-5 family (Linux lane, server2; after S1)

Files:
- `tests/Antiphon.E2E/ChannelOutboundIsolatedTests.Composed.cs`: `root` becomes `OwnedFixtureRoot.Create("c0418-f5", Temp)`, which is `/var/tmp` on Linux. `repo`, `repo-q`, `converter`, `native`, `outbound`, `fakegrok-cli`, `fixture-chromium` and `converter-gate` stay operational. The `finally` at `:497-501` copies the evidence through `RetainEvidence` into `<checkout>/.antiphon/test-output/card-0418/f5/<guid>/` and then disposes the root. Evidence means `source-report.md`, `channel-answer*.md` and the outbound store's regular files, never `repo*`. The browser wrapper keeps `-v root:root`; the in-container dockerd sees `/var/tmp` (section 1).
- New `tests/Antiphon.E2E/C1954FixtureLayoutTests.cs`. Its method `Composed_layout_keeps_git_fixtures_off_the_checkout` runs a small extracted layout builder (the `git init`/`commit` of the two repos, no broker and no browser), and asserts the root base, an unchanged checkout census, and root absence after dispose. It is green-only, new API.
- `tests/derived-impact-index.json` (regenerated, last).

Witnesses:
- **W-4 (red-capable, not executed by default):** a before/after census of `<checkout>/.antiphon/test-output/card-0418/f5` inside `Four_sources_convert_only_for_the_selected_conversation`. It runs only under the three opt-ins plus Docker, at 9m01s and 15m47s in historical samples (section 4). Executing it red at base doubles that cost, so the red run happens only if the operator grants two runs (Q-3).

### S3: LandDelivery family (Linux witness on server2; Windows canary on the desktop; after S1)

Files:
- `tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs`: the owner's `Root` becomes `OwnedFixtureRoot.Create("c467-land", PersistentDisposable)`. The borrowed `-second` instance (`:40`) keeps the owner's root and **never** deletes it. The owner's `DisposeAsync` (`:432-447`) keeps the snapshot, child kill and app dispose. Then, in `finally`, it copies the evidence and disposes the root. Evidence means every top-level regular file of `Root` (`delivery-evidence*.json`, `protocol-git-*.json`, `protocol-verify-*.json`, `git-trace.txt`, `wait-*.json`, `attention-*.json`, `identities*.json`, `loaded-runner*.json`, `status-polls.txt`, `script-acceptance.txt`, `http-202.json`, `child-*.log`, `snapshot-failure.txt`, `dispatch-task.txt`) plus the regular files under `server-logs/` and `native/`. It never copies `repo*`, `trees/`, `remote*.git` or `caller*`. The destination is `<checkout>/.antiphon/acceptance/card-0467/<same guid>/`. The `:446` comment is replaced.
- `tests/Antiphon.E2E/Fixtures/OwnedLandServerChild.cs:14`: uses `OwnedFixtureRoot.Owns(args[0], "c467-land")`.
- `tests/Antiphon.E2E/LandDeliveryFixtureProbeTests.cs`: new method `C1954_Probe_root_is_owned_outside_checkout`. It constructs `new LandDeliveryFixture()` without `InitializeAsync`, so it compiles and runs on Linux at base. It writes a protocol record, `repo/.git/HEAD` and `remote.git/HEAD` into `Root`, then disposes. It asserts that `Root` is not under the checkout, that the root is absent after dispose, that the evidence directory holds the protocol record, and that the evidence holds no `HEAD` or `.git`.
- Disposal order risk: `await using` disposes the borrower before the owner (reverse declaration order, `AgentTaskLandDeliveryE2ETests.cs:33,41`). The borrower's snapshot writes happen while the root still exists. Code asserts this order holds in V23 and the `:376` site.

Witnesses:
- **W-5 (red at base):** `C1954_Probe_root_is_owned_outside_checkout`. Red at base: `Root` is under `<checkout>/.antiphon/acceptance/card-0467`.
- Windows canary (desktop lane): `C467_V22` (owner only) and `C467_V23` (owner plus borrower). They prove the persistent-disposable base on real Windows ConPTY, with long paths (`core.longpaths` is kept) and report-store persistence, and that `OwnedLandServerChild` accepts the new root.

### S4: C419 apply canary family (Windows-only; after S3)

Files:
- `tests/Antiphon.E2E/OutputDistillationApplyCanaryTests.cs:51`: `root` becomes `OwnedFixtureRoot.Create("c419-apply", PersistentDisposable)`. The `finally` at `:181-185` writes `teardown.json` and then copies `apply-acceptance.md`, `teardown.json`, the ledger/transcript JSON files the test writes, and other top-level regular files into `<checkout>/.antiphon/acceptance/card-0419/<guid>/`. It never copies `repo/` or `worktrees/`. Then it disposes. `apply-acceptance.md` names the **evidence** path, not the operational root.
- `tests/Antiphon.E2E/Fixtures/DistillerCanaryOptions.cs:15`: uses `OwnedFixtureRoot.Owns(Root, "c419-apply")` and keeps the `.git` requirement.
- `C1954FixtureLayoutTests`: new method `Distiller_options_accept_only_an_owned_persistent_root`. It calls the ownership check directly, without `Validate` (which needs live opt-ins), and runs on both lanes. It is green-only.

Witnesses: the live canary (`ANTIPHON_DISTILLER_APPLY_CANARY`, live Claude spend, approval record) is **not** run by Code (Q-4). S4's real-path evidence is limited to the helper and ownership tests on Windows and Linux. That is a stated gap, not a pass.

### S5: docs (any lane; after S1, can ride with S4)

- `docs/testing-and-build.md`: a new subsection, "Fixture roots vs retained evidence (CARD-1954)". It states: Git or operational fixtures go in `OwnedFixtureRoot` (`/var/tmp`, or `%TEMP%`, or the persistent base); never `/tmp` or the checkout on Linux; retained evidence is plain files through `RetainEvidence`; the audit is unchanged and refuses anything else. No `docs/docker-stack.md` edit (collision with CARD-1957 and the docs task). The selector paragraph at `docker-stack.md:444-452` already says new fixtures belong in system temp.

### Existing tests affected

No migration, public DTO/route, operation label, remote script function or statement budget changes. Rows that `scripts/impacted-classes.ps1 -Range` will select (CARD-1782):
- Timing labels: the new helper adds `BeginPhase(` under `tests/**`, which selects `TestTimingOperationLabelTests`.
- `linux-only-policy`: selected only if Code adds `[NativeLinuxOnly(` (W-1 note).
- Directly affected classes: `LinuxOwnedGitProcessTests`, `AgentTaskLandNotificationRecoveryTests` (the `C467_V09` and two `C488_*` delegates), `TestDbFixtureLazyInitializationTests`, `LandDeliveryFixtureProbeTests`, `AgentTaskLandDeliveryE2ETests` (Windows), `ChannelOutboundIsolatedTests` (opt-in), `OutputDistillationApplyCanaryTests` (Windows, live), and the source pins `NativeWindowsFixtureGateTests`, `LandDeliveryWaitRoutingTests`, `DispatchBaseWarningRosterTests` and `ImpactedClassesSelectorTests` (`:572-574`, `:1004` name `LandDeliveryFixtureProbeTests`).

## 4. Performance budget

Measurement sources:
- CARD-1953 on server2 (2026-11-15, loaded, single samples, provisional): `Antiphon.Tests` build 221 s; the eleven-class guard union 213-218 s; `ImpactedClassesSelectorTests` 260.5 s; `WindowsUnitPlatformAdmissionTests` 34.5 s.
- Composed F-5 (2026-09-28, Linux, two samples): 9m01s and 15m47s.
- `LinuxOwnedGitProcessTests` CP-2 (CARD task 0485dc70, 2026-11-13): 35 tests green, with no duration recorded. Per-test `[Timeout(10_000)]` stays.
- No measurement exists for the C467 queue-race trio, the E2E build on server2, the `LandDeliveryFixtureProbeTests` class or the Windows V22/V23 rows. These estimates are **provisional**. Code records the TRX per-test durations and phase JSONL, and TestDesign/Review re-derives budgets at about 2x the measured p95.

| Selection | Expected | Budget / timeout | Basis |
|---|---|---|---|
| `C1954FixtureRootTests` (about 14 cases including `[Arguments]`) | < 15 s total; each case under 2 s (file I/O plus at most one `git init`) | `[Timeout(30_000)]` per test, provisional | no sample; replace with 2x p95 after the first green row |
| `LinuxOwnedGitProcessTests` class | about 2 min | existing 10 s per test, unchanged | CP-2 count only |
| C467 queue-race trio | 3 to 5 min (each case spawns two `dotnet` children with existing 90/100 s budgets) | unchanged | provisional |
| `TestDbFixtureLazyInitializationTests` `(A_*)\|(Mixed_*)` | < 2 min | existing 60 s assertion | provisional |
| Guard union and selector | about 4 + 4.5 min | row estimates 8 and 9 min | CARD-1953 |
| E2E `C1954FixtureLayoutTests` + `LandDeliveryFixtureProbeTests` | < 4 min | no new per-test timeout beyond the helper's 30 s | provisional |
| Composed F-5 opt-in (manual row) | 9-16 min | its existing 25-minute internal deadline, unchanged; row 20 min | two samples |
| Windows V22 + V23 | provisional 10 min | existing fixture waits unchanged | no sample |

Phases: shared setup stays where it is. TestDbFixture is once per assembly. Composed owns its broker and browser image. No new once-per-run setup is added. New teardown time is reported as `FixtureTeardown/fixture-root-delete`, and the evidence copy as `FixtureTeardown/fixture-evidence-copy`. Build and slot wait are reported separately by `build-slot.ps1` and `BUILD SLOT` lines. Nothing approaches 30 minutes. If a row exceeds 30 minutes it goes to Debug (CARD-0110 rule). Artifacts: rows under the checkpoint run folder (ignored); the TRX carries per-test durations.

## 5. Verification design

V-1 placement: every family's operational root is outside the checkout, and on Linux directly under `/var/tmp`. V-2 disposal: the exact root is absent after success, assertion failure, setup failure and cancellation. V-3 fail-closed ownership: child and option checks refuse unowned roots (W-3). V-4 evidence: retained evidence exists at D-6, holds only regular files, and has no Git-shaped names. V-5 no regression: guards, source pins and impacted rows are green.
R-1: `scripts/check-evidence-diff.ps1 -Repository . -BaseRef <base> -HeadRef HEAD` reports 0 violations. R-2: `git merge-tree --write-tree origin/master HEAD` is clean. R-3: `impacted-classes.ps1 -Range <base>..HEAD` rows are executed or listed as owed on Windows.

Lanes: CP-1 to CP-8 run on the Linux lane (server2, the global default from `GET /api/runner-defaults`). Dispatch them with no `-Runner` and no `-Platform`. The Windows rows run on the desktop lane, dispatched with `-Platform Windows`. Its table is separate so the Linux import never sees it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-red | tests/Antiphon.Tests -> bin-c1954-red/ | s1-red-witness | `/*/*/C1954FixtureRootTests/Containment_fixture_root_is_outside_checkout_and_removed` | W-1, V-1 | 1 intended assertion failure (path under checkout) | 1 | 6 |
| CP-2 | S1-red | CP-1 | s1-red-queue | `/*/*/AgentTaskLandNotificationRecoveryTests/(C467_V09*)\|(C488_ApprovalReceiptQueueKey*)\|(C488_ApprovalReceiptKeyCollision*)` | W-2 | 3 intended census assertion failures | 3 | 6 |
| CP-3 | S1 | tests/Antiphon.Tests -> bin-c1954-s1/ | s1-green | `/*/*/(C1954FixtureRootTests*)\|(LinuxOwnedGitProcessTests*)/*` | W-1, V-1, V-2, V-3, V-4 | all listed, 0 failed, 0 skipped | 45 | 6 |
| CP-4 | S1 | CP-3 | s1-queue | `/*/*/AgentTaskLandNotificationRecoveryTests/(C467_V09*)\|(C488_ApprovalReceiptQueueKey*)\|(C488_ApprovalReceiptKeyCollision*)` | W-2, V-2 | 3 passed, 0 failed | 3 | 6 |
| CP-5 | S1 | CP-3 | s1-worker-refusal | `/*/*/TestDbFixtureLazyInitializationTests/(A_*)\|(Mixed_*)` | W-3, V-3 | all listed, 0 failed, 0 skipped | 2 | 3 |
| CP-6 | S1 | CP-3 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(TestTimingOperationLabelTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | V-5 | all listed, 0 failed; WindowsUnit platform skip allowed only as recorded by CARD-1953 | 10 | 8 |
| CP-7 | S3 | CP-3 | source-pins | `/*/*/(NativeWindowsFixtureGateTests*)\|(LandDeliveryWaitRoutingTests*)\|(DispatchBaseWarningRosterTests*)\|(ImpactedClassesSelectorTests*)/*` | V-5 | all listed, 0 failed | 4 | 9 |
| CP-8 | S3-red | tests/Antiphon.E2E -> bin-c1954-e2e-red/ | s3-red-witness | `/*/*/LandDeliveryFixtureProbeTests/C1954_Probe_root_is_owned_outside_checkout` | W-5, V-1 | 1 intended assertion failure | 1 | 10 |
| CP-9 | S4 | tests/Antiphon.E2E -> bin-c1954-e2e/ | e2e-green | `/*/*/(C1954FixtureLayoutTests*)\|(LandDeliveryFixtureProbeTests*)/*` | V-1, V-2, V-3, V-4 | all listed, 0 failed | 17 | 12 |

Suffix check, done at `870a93d21`: `C467_V09*`, `C488_ApprovalReceiptQueueKey*` and `C488_ApprovalReceiptKeyCollision*` each match exactly one method across the three `AgentTaskLandNotificationRecoveryTests` partial files. No other class name begins with `C1954FixtureRootTests`, `C1954FixtureLayoutTests`, `LinuxOwnedGitProcessTests` or `LandDeliveryFixtureProbeTests`. Code re-checks this after adding methods. CP-3's `Min` assumes 35 + about 10; Code sets it to the counted cases.

CP-7 builds `Antiphon.Tests` at the S3 tip; it reuses CP-3's build only if S3 changes no `Antiphon.Tests` source. Otherwise Code adds an isolated build for it. Per the checkpoint import rules, a build is never reused across `After` groups whose source differs.

Manual opt-in row, not in the table (operator Q-3): CP-M1 `/*/*/ChannelOutboundIsolatedTests/Four_sources_convert_only_for_the_selected_conversation`. It reuses CP-9's build, with `ANTIPHON_HEADED_TESTS=1`, `ANTIPHON_BROKER_TESTS=1` and `ANTIPHON_CHANNEL_OUTBOUND_ISOLATED=1`, `client/dist` rebuilt first, on server2. Expect 1 passed with the W-4 census green. Estimate 20 min.

### Windows-lane checkpoints (desktop, `-Platform Windows`)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-W1 | S1 | tests/Antiphon.Tests -> bin-c1954-win/ | win-helper | `/*/*/C1954FixtureRootTests/*` | V-1, V-2 (Windows `%TEMP%` and persistent base, read-only Git objects) | all listed, 0 failed | 14 | 10 |
| CP-W2 | S4 | tests/Antiphon.E2E -> bin-c1954-win-e2e/ | win-land | `/*/*/AgentTaskLandDeliveryE2ETests/(C467_V22*)\|(C467_V23*)` | S3 canary: persistent base, borrower, child owner check | 2 passed | 2 | 15 |
| CP-W3 | S4 | CP-W2 | win-e2e-probes | `/*/*/(C1954FixtureLayoutTests*)\|(LandDeliveryFixtureProbeTests*)/*` | V-1 to V-4 on Windows | all listed, 0 failed | 17 | 8 |

## 6. Risks and operator decisions

Risks:
- **R-1:** `/var/tmp` is the container's writable layer on server2. A leaked root (a killed test host) costs container-layer disk until the container is recreated, and it is then destroyed. It never blocks retirement. Accepted. This is the CARD-1901/1953 precedent.
- **R-2:** On a developer Linux machine or the nightly checkout, `/var/tmp` is a host path that a crash leaks into indefinitely. The helper prefix `antiphon-fixture-` makes leftovers identifiable. No sweep is added (out of scope; CARD-1677 and the host cleanup own sweeps).
- **R-3:** The Composed browser bind (`-v root:root`) depends on dockerd seeing `/var/tmp`. On server2, dockerd runs inside the runner container (`docker-compose.server2-runner.yml:112`), so it does. This has not been executed: **UNOBSERVED** until CP-M1 runs.
- **R-4:** The persistent Windows base `%LOCALAPPDATA%\Antiphon\fixture-roots` is a new location, and Defender scanning or a path quirk could slow V22/V23. CP-W2 is the evidence.
- **R-5:** Producers outside this card can still put Git state in a checkout when killed mid-run: `ReportWorkspace` (deletes on dispose), and possibly `MarkdownPdfRealBrowserTests` (uninspected). Gate 9 stays the backstop and will stop-and-report them.
- **R-6:** Three near-identical owned-root implementations remain (`ComposeFixtureRoot`, `VolumeRetirementPublicationFixture`, the new helper). Consolidation is a follow-up.
- **R-7:** `tests/derived-impact-index.json` conflicts at land with any concurrent test-adding branch. Resolve by regeneration on the batch tree.

Operator questions (yes/no, with recommendation):
- **Q-1:** Keep the retirement audit byte-for-byte unchanged (no marker, path or naming classifier), and solve this only by moving fixture roots? **Recommend yes.** A classifier would weaken the guard that protects real unpublished work (D-1).
- **Q-2:** Keep the two Windows-only families (S3 LandDelivery, S4 C419) in CARD-1954, even though they can never reach a retired runner volume? **Recommend yes, dispatched after S1/S2.** The card names them, and they leak real repos into desktop checkouts. Splitting them to a follow-up card is an acceptable alternative.
- **Q-3:** Authorise one opt-in Composed F-5 run on server2 (Docker, Redpanda, browser image build, about 10-16 min) as CP-M1? A second, red run at base would double that. **Recommend yes to one green run, no to the red-at-base run.**
- **Q-4:** Run the live C419 apply canary (live Claude spend and approval record) to prove S4 end to end? **Recommend no** for this card. The helper and ownership witnesses on both lanes suffice, and the gap is stated.
- **Q-5:** Authorise any cleanup of existing leftover roots in live checkouts or volumes as part of this card? **Recommend no.** Gate 9 stop-and-report and the recorded operator manual path (O-2) stay the only removal route (D-8).
- **Q-6:** Add a source guard that fails when a test creates Git state under a `FindRepositoryRoot()`/`.antiphon` path? **Recommend no** for now (fragile text matching). Revisit as a follow-up if a sixth producer appears.

Defaults if unanswered: Q-1 yes, Q-2 yes, Q-3 one green run, Q-4 no, Q-5 no, Q-6 no.

Standing authority used: none beyond plan writing. Mutation is waived (operator 2026-11-12), so PC rows are recorded as pending, not run.
