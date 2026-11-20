# Unowned rc-input paths, 2026-11-20

Census at `ba9ffc8ccc5496ef849af656e1d2906b7d308e43` reports 202 unowned tracked paths. A proposed noop and owner patch clears 170 of them. 32 stay unowned: 31 need a decision, and 1 is test source that the test-only class does not remove from the unowned set. The orchestrator flag stays on after that patch because 18 unresolved closure rows set the same `creditBlockedBy`.

## Census

`pwsh -NoProfile -File scripts/rc-input-reader-census.ps1` exited 0. Load was 12.23 when it started, under the server2 limit of 16. It finished inside the 500 second bound.

| Line | Value |
|---|---|
| paths-checked | 11214 |
| suites-checked | 27 |
| pairs-checked | 10242 |
| under-selected | 0 |
| unanalysable | 0 |
| noop-conflict | 0 |
| noop-vs-owner | 0 |
| unowned-count | 202 |
| unresolved-count | 18 |
| creditBlockedBy | unowned-paths |
| needsOrchestratorDecision | True |
| repo-walk | none |

The full list is the census file `.antiphon/rc-unowned-paths.txt` (hint, tab, path). That file is generated evidence and is not part of this commit.

Unowned is computed after owner roots, project closure, and the noop list. A path that is in neither an input map nor the noop list is unowned (`scripts/lib/rc-input-manifest.ps1:284-295`). Any unowned path or unresolved row sets `needsOrchestratorDecision` and `creditBlockedBy=unowned-paths` (`:296-306`). `testOnly.paths` is not consulted there (`:285-286`). Adding a test-only path does not clear the flag.

Two paths the project closure already owns were not in the 202:

- `src/Messaging.Pack.props`, imported by the messaging csproj files (for example `src/Antiphon.Messaging/Antiphon.Messaging.csproj:3`).
- `tools/Antiphon.NativeLinuxMigrationAudit/NativeLinuxWindowsReasonAudit.cs`, compiled into Antiphon.Tests (`tests/Antiphon.Tests/Antiphon.Tests.csproj:186`).

## Classes

| Class | Paths | Patch | Still unowned |
|---|---:|---|---:|
| NO-OP | 164 | 28 rows in `rc-input-noop.json` | 0 |
| OWNED-BY-SUITE | 6 | 6 roots in `rc-input-owners.json` | 0 |
| TEST-ONLY | 1 | `testOnly.paths` only | 1 |
| NEEDS-DECISION | 31 | withheld | 31 |
| Total | 202 | clears 170 | 32 |

The patch file is `docs/investigations/2026-11-20-unowned-paths-triage.patch.json`. It is not applied to `scripts/lib/rc-input-noop.json` or `scripts/lib/rc-input-owners.json`.

Each noop glob was checked against `git ls-files`. All 28 globs together match 164 tracked paths, all 164 are in the census unowned list, and none match an existing owner root. In particular they do not match `_bmad/_config/manifest.yaml` or any `**/*.md` path. A noop that matched an owner root would fail the census with `noop-vs-owner`.

## NO-OP (164)

### `_bmad` non-markdown, 132, plus `_bmad-output` non-markdown, 2

Tracked `_bmad` is 1147 files: 1014 markdown, already owned by `**/*.md`, plus `_bmad/_config/manifest.yaml`, already an Antiphon.Tests root. The other 132 files are csv, yaml, html, css, js, json, and one groovy template. `_bmad-output` adds `sprint-status.yaml` and `ux-design-directions.html`. Its 8 markdown files stay on `**/*.md`.

Suite and nightly proof: a search for `_bmad` in `tests/**/*.cs`, `scripts/**/*.ps1` (including `scripts/lib/nightly-*.ps1` and `scripts/nightly-*.ps1`), `src`, `server`, `client`, `docker`, `tools`, `.github`, and `Antiphon.AppHost` finds no path. The only product hit outside `_bmad` itself is the existing owner row in `scripts/lib/rc-input-owners.json:64`.

Harness proof, which does not make these suite inputs: `.claude/skills/bmad-validate-prd/SKILL.md:6` tells the agent to load `{project-root}/_bmad/bmm/workflows/.../workflow-validate-prd.md`. Those targets are markdown and are already owned. The nightly scripts do not reference `_bmad`.

### `.antiphon` CARD-0959 archives, 26

`.antiphon/c959-code-evidence` (2), `.antiphon/c959-cont-evidence` (10), and the non-markdown files under `.antiphon/c959-second-evidence` (14). No test, script, or project file names those directories. `README.md` and `mutation-pending.md` in the second directory stay on `**/*.md`, so the patch uses extension globs there.

### Single files, 4

| Path | Proof |
|---|---|
| `LICENSE` | No `.cs`, `.ps1`, `.json`, `.yml`, `.csproj`, Dockerfile, or script contains the path. |
| `.pts-vibe/config.yml` | No suite, script, or project file contains `.pts-vibe`. `tests/Antiphon.Tests/CachingChatClientTests.cs:71` is a synthetic `/home/user/project/config.yml` string. `.pts-vibe/README.md` stays on `**/*.md`. |
| `tools/Antiphon.NativeLinuxMigrationAudit/Program.cs` | Not a Compile item. The CARD-1613 plan says normal tests, discovery, and qualification never invoke the tool. |
| `tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj` | No ProjectReference. `scripts/test-rc-input-owners.ps1:496` writes a synthetic copy in a temp repository. |

## OWNED-BY-SUITE (6)

All six are read from the real checkout by a suite test. The census literal scan skips literals that start with `.`, so the dot-paths stayed unowned until this triage.

| Path | Suite | Reader |
|---|---|---|
| `.antiphon/fixtures/card-1521/partial-boot-pointers.approved.json` | Antiphon.Tests | `tests/Antiphon.Tests/Application/SessionMessageQueueCodexPointerTests.cs:298` walks parents and `ReadAllText`s the file. Also `tests/derived-impact-index.json:2414`. |
| `.config/dotnet-tools.json` | Antiphon.Tests | `tests/Antiphon.Tests/Infrastructure/DotnetToolManifestContractTests.cs:17` via `DockerStackDocuments.Read` (`tests/Antiphon.Tests/Infrastructure/DockerStackDocuments.cs:12-13`). |
| `.dockerignore` | Antiphon.Tests | `tests/Antiphon.Tests/Infrastructure/DockerStackContractTests.cs:63` via the same `Read`. |
| `.gitattributes` | Antiphon.Tests | `tests/Antiphon.Tests/Infrastructure/ShellFixtureLineEndingContractTests.cs:26` and `SessionRunnerGrokLineEndingContractTests.cs:22`. |
| `restart-server.ps1` | Antiphon.Tests | `tests/Antiphon.Tests/Scripts/NightlyScriptsTests.cs:70-76` reads the repo file bytes. `ToolchainFloorWarningTests.cs:127` expects the toolchain harness to parse it. |
| `tests/live-lanes.json` | Antiphon.Tests and Antiphon.Agents.Pty.Tests | `LiveLaneInventoryTests.cs:19-21` runs case `ExactCoreRoster`. `LiveClaudeInventoryTests.cs:17-19` runs `ExactPtyCoreRoster`. Both pass the real repo to `scripts/test-live-lane-manifest.ps1:515-516`, and `Invoke-LiveLaneRosterCase` calls `Read-LiveLaneManifestObject` (`scripts/lib/live-lane-manifest.ps1:154-158`). |

Owning `.gitattributes` removes it from the unowned list only. The tree walk still records `external-content-contract:.gitattributes` (`rc-input-manifest.ps1:96`), and that unresolved row keeps the flag.

## TEST-ONLY (1)

`tests/card-file-privacy-controls.py`. The file's own header is the usage line. No `.cs` or `.ps1` names it. `docs/superpowers/plans/2026-09-06-card-0408-code-evidence.md:148` describes a manual run in an isolated worktree. There is no product reader.

The patch adds it to `testOnly.paths`. That records the class and does not remove the path from unowned, so it counts in the residue of 32. A noop row would clear it. That row is withheld because the class is test source rather than a proven no-op archive.

## NEEDS-DECISION (31)

These are read by a script, or they are executable entry points, and no suite test reads the path. A noop would hide an input that can change a script test or a local run. An owner root would name a suite that does not read the file. `consumers: *` would select every suite and is not proposed here.

### Root contracts and dev entry points, 18

| Path | Why it is not a noop |
|---|---|
| `.gitignore` | No test `ReadAllText`s the repository file (suite hits write a scratch `.gitignore`). `scripts/lib/nightly-policy.ps1:115` runs `git ls-files --ignored`, which applies this file to nightly project discovery. |
| `assert-dev-ports-clear.ps1` | Called by `restart-server.ps1:29` and `restart-client.ps1:21`. `scripts/test-toolchain-floor.ps1:159` writes a stub and does not read the repo file. |
| `dev-backup.ps1` | Called from `dev-upgrade.ps1:16`, `dev-restore.ps1:27`, and named by `dev-start.ps1:77`. |
| `dev-start.ps1` | Called from `dev-fresh.ps1:48` and `dev-upgrade.ps1`. `verify-dev-stack.ps1:8` names it in help text. |
| `dev-stop.ps1` | Called from `dev-fresh.ps1:32`, `dev-restore.ps1:42`, and `dev-upgrade.ps1:20`. |
| `restart-client.ps1` | Called from `restart.ps1:25`. |
| `restart.cmd` | Named by `verify-dev-stack.ps1:95` and `scripts/bootstrap-check.ps1:340` as the Docker Desktop recovery hint. |
| `dev-fresh.ps1`, `dev-restore.ps1`, `dev-upgrade.ps1`, `restart.ps1` | They call the scripts above. No suite reads their bytes. |
| `approve-gate.ps1`, `check-artifacts.ps1`, `check-workflow.ps1`, `create-workflow.ps1`, `monitor-workflow.ps1`, `stop-server.ps1`, `test-api.ps1` | No test, `scripts/**/*.ps1`, or other root script names them. They are still operator entry points, so a silent noop is not proven. |

### Client script fixtures, 6

No `tests/**/*.cs` names these paths. Script gates do:

- `scripts/test-client-typecheck.ps1:74` runs the typecheck gate on `tests/fixtures/card1387-typecheck/tsconfig.json`.
- `scripts/test-client-gate-exit.ps1:43-45` points at that tsconfig, `tests/fixtures/card1398-typecheck-ok/tsconfig.json`, and `tests/fixtures/card1393-lint/bad.test.ts`.
- `scripts/test-client-lint.ps1:35` points at `tests/fixtures/card1393-lint/bad.test.ts`.

The other three files in those directories have no literal reader: `tests/fixtures/card1387-typecheck/bad.test.ts`, `tests/fixtures/card1393-lint/eslint.config.mjs`, and `tests/fixtures/card1398-typecheck-ok/ok.ts`. `tsc` and eslint read them when the gate runs on the tsconfig or the lint fixture, so they stay with the directory.

### CARD-0490 guest files, 4

`tests/fixtures/card0490-linux/assets.lock.json` is the default lock for `scripts/build-card0490-bootdisk.ps1:19`, `scripts/prepare-card0490-linux-assets.ps1:3`, and `scripts/verify-phone-home-grok.ps1:23`. The example json already owned at `card0490-live.example.json:23` points at it. `GrokRunnerImageContractTests` reads the example json, not the lock.

`check-guard.sh`, `guest-init.sh`, and `run-one.sh` name only each other (`check-guard.sh` execs `guest-init.sh`). No test, script, or Dockerfile names them. They are the guest programs for that lane, so a noop is not proven.

### Live-lane data the roster cases do not read, 3

| Path | Reader that is not a suite literal |
|---|---|
| `tests/fixtures/live-lanes/full-discovery-before.json` | `scripts/lib/live-lane-manifest.ps1:9`. The S1a group copies that git blob (`scripts/test-live-lane-manifest.ps1:158-166`). `CensusTargetScope` and `CensusAdditiveIdentity` use a scratch repo and do not read this file. |
| `tests/fixtures/live-lanes/pre-s1-base.sha` | `Get-LiveLanePinnedPreS1` reads it (`live-lane-manifest.ps1:67-69`) when census acceptance runs against `candidateRoot`. |
| `tests/live-lane-inputs.json` | `scripts/lib/live-lane-fingerprint.ps1:8`. The fingerprint test scripts write synthetic copies. No `tests/**/*.cs` names the path. |

## Unresolved rows that keep the flag

These 18 are independent of the unowned list. Clearing all 202 unowned paths would still leave `creditBlockedBy=unowned-paths`.

- `dynamic-build` on AppHost, both root Directory.Build files, six test csproj files, and `tests/Shared/CheckpointRepeat.targets` plus `FakeProviderOutput.targets`.
- `external-content-contract` on `.gitattributes`, `scripts/fixtures/qualification-graph/s1/.gitattributes`, and `tests/Antiphon.Agents.Pty.Tests/golden/.gitattributes`.
- `missing-input` on the two FakeGateway PublicAPI txt files.
- `unresolved-compile` on `tests/Shared/CheckpointRepeat.targets` and `unresolved-content` on `src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`.

## Not done, noted

The patch is not applied to the selector maps.

## Residue

After the noop rows and the six owner roots, unowned count is 32. The flag remains, both for those 32 paths and for the 18 unresolved rows.
