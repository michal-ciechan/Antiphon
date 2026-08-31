# CARD-0270 — Put the `am-service` remote deploy recipe under source control

**Date:** 2026-08-31  
**Status:** Plan only. No production command, script, or documentation rewrite was run in this pass.

## Decision

Create one target-specific, production-gated command:

~~~powershell
pwsh -NoProfile -File scripts/deploy-am-service.ps1
~~~

It derives the archive manifest from the tracked local Dockerfile's `COPY` sources, never from a separately maintained project list. The local Dockerfile is the source of truth because it is itself included in the `Antiphon.Messaging.Service/` directory copied to server2, then used by the remote Compose build. A dependency added to a `COPY` line therefore enters the archive without a second edit; unsupported syntax fails before upload instead of silently deploying an incomplete tree.

Do not generalize this into a target framework yet. The only source-built remote target owned by this repository is `am-service`. Server2's `school_revision` stack deploys a published `ghcr.io/michal-ciechan/antiphon-messaging-telegram:2026-07-19` image and belongs to a different deployment surface; it has no source-tree sync and must not inherit an am-service tar recipe. Extract a shared deployment module only after a second in-repo target needs the same transaction.

## Ground truth verified on server2

The live target is `mc@server2:/home/mc/antiphon-messaging`:

| Fact | Verified value |
|---|---|
| Compose service / container | `messaging-service` / `am-service` |
| Compose build context | `./build/src` (resolved to `/home/mc/antiphon-messaging/build/src`) |
| Compose Dockerfile | `Antiphon.Messaging.Service/Dockerfile` |
| Dockerfile source | `src/Antiphon.Messaging.Service/Dockerfile` in this repository |
| Dockerfile parity | Local and server2 SHA-256 are both `3ee16542ae3eec012e53921c3efb9875c344546f9101947722190d0f300cdfc3` |
| Remote tooling | Docker Compose v2.18.1, GNU tar 1.29, Python 3 available for safe Compose JSON projection |

The current Dockerfile's build-stage `COPY` instructions require these six source-root entries:

~~~text
Messaging.Pack.props
Antiphon.Messaging/
Antiphon.Messaging.Gateway/
Antiphon.Messaging.Telegram/
Antiphon.Messaging.Slack/
Antiphon.Messaging.Service/
~~~

This is evidence, not a new hard-coded manifest. CARD-0265 failed because the old memory recipe omitted `Antiphon.Messaging.Gateway/` and `Antiphon.Messaging.Slack/`. The implementation must delete the equivalent static list now present in `docs/telegram-bot-ops.md` rather than merely adding a better list beside it.

The service's Compose file keeps tokens in remote environment interpolation. The script must never render or log the entire resolved Compose config. Its read-only preflight will project only the selected service's `build.context` and `build.dockerfile` through remote Python, using:

~~~bash
docker compose config --no-interpolate --format json messaging-service | python3 ...
~~~

That gives the script the actual remote build contract without exposing Telegram, Slack, or database environment values.

## Target contract and command surface

`scripts/deploy-am-service.ps1` is a fixed, ASCII-only PowerShell front door, modelled after the verdict discipline in `scripts/deploy-local.ps1`. It owns exactly these target facts:

~~~text
SSH host:             mc@server2
Remote deployment dir: /home/mc/antiphon-messaging
Compose service:       messaging-service
Container:             am-service
Local Docker context:  <repo>/src
Remote Docker context: build/src
~~~

Those are deployment-address facts, not a project dependency list. Before any write the script must query the remote Compose service and refuse if its resolved build context or Dockerfile does not equal the expected target contract. This detects a remote Compose change that a local script cannot safely guess at.

| Parameter / mode | Effect |
|---|---|
| default or `-WhatIf` | Read local Dockerfile and remote build projection, validate the archive manifest, and print intended source entries. No SCP, remote extraction, build, recreate, or real-message check. |
| `-Deploy` | Required explicit opt-in for every remote write. Combine with PowerShell `-Confirm` for an interactive deployment; a Deploy-role brief must state that production deployment is authorized before using it non-interactively. |
| `-SkipRealTrafficCheck` | Explicitly records that the operator must perform the existing test-group round trip after technical verification. It does not make a live Family-group test legal. |
| `-TimeoutSec` | Bounds polling for the recreated service and HTTP endpoint; do not widen an SSH/build failure into a blind wait. |

Use `[CmdletBinding(SupportsShouldProcess, ConfirmImpact='High')]`. `-Deploy` plus `ShouldProcess` makes the externally visible operation obvious in both an interactive session and a stored Deploy brief. The final line is always one of:

~~~text
REMOTE DEPLOY VERDICT: ok
REMOTE DEPLOY VERDICT: failed <phase and safe diagnostic>
~~~

Do not accept a generic `-Host`, arbitrary remote command, arbitrary context root, or arbitrary service switch in this first version. Such flexibility would turn a carefully scoped recipe into a remote shell launcher before there is a second target to prove its design.

## Dockerfile-derived manifest

Implement the parser as pure functions within the script (or an adjacent private module only if that proves necessary for testability):

1. Read `src/Antiphon.Messaging.Service/Dockerfile`, join escaped continuation lines, strip blank lines/comments, and locate `COPY` instructions.
2. Ignore only `COPY --from=...` instructions: those copy between image stages, not from the local build context. For every local `COPY`, take all source tokens except the final destination.
3. Support the current shell-form static relative paths. Deduplicate while preserving Dockerfile order, trim a cosmetic trailing slash, and retain both files and directories.
4. Refuse, before archive creation, JSON-array form, variable substitution, globbing, absolute paths, `..`, an unrecognised option, or any source that resolves outside `<repo>/src` or does not exist. A future Dockerfile using one of those constructs must extend the parser and its tests in the same change; falling back to an incomplete archive recreates CARD-0265.
5. Build the tar from `<repo>/src` using the derived relative entries. Exclude `bin`, `obj`, and `bin-` at every depth, then list the archive and reject it if any forbidden output directory is present. Also assert every derived root entry appears in the archive.
6. Include the Dockerfile's SHA-256 and the manifest in the dry-run/deploy report. Do not write a separate checked-in manifest file.

The parser must not use project names or `.csproj` discovery as a fallback. A project referenced only indirectly is not necessarily in Docker's context; conversely a props file or future source asset can be required by Docker without being a project. `COPY` is the build contract.

## Deployment transaction

### Preflight (all modes, read-only)

1. Resolve the repository root from `$PSScriptRoot`; verify `ssh`, `scp`, and `tar` exist locally.
2. Verify the local context, Dockerfile, and every derived source entry. Fail with the source line and path for a missing item.
3. Run the narrow remote Compose JSON projection. Verify that `messaging-service` still points at `/home/mc/antiphon-messaging/build/src` and `Antiphon.Messaging.Service/Dockerfile`; print only those two values.
4. Capture safe pre-deploy facts for the report: current `am-service` container/image ID, Compose service status, and the local Dockerfile hash. Do not print environment/config output or token values.

### Archive and remote replacement (`-Deploy` only)

1. Create a uniquely named temporary archive locally and an equally unique `/tmp` upload name on server2. Use process exit codes, not a terminal pipeline's last consumer, as every verdict.
2. Upload the archive. Remote code creates a staging directory beneath the deployment root, extracts there, verifies all manifest entries and the Dockerfile hash, and only then swaps the staged tree for `build/src`.
3. Rename the previous `build/src` to a UTC-stamped `build/src.bak-*` sibling. Do not overlay the old tree: deleted or renamed files must disappear from the next build. Keep the backup and emit its exact path in the report until the deployment is accepted.
4. Run `docker compose build messaging-service`, then `docker compose up -d --no-deps messaging-service`. The previous running container stays untouched through archive creation, upload, extraction, and image build; the recreate is the only planned service interruption.
5. On any failure before `up`, leave the current container running and report the staged/backup paths. On failure during recreate or verification, do **not** auto-rollback: Compose image/tag state and an uncertain health signal make an automatic reverse recreate more dangerous than a clear human decision. Report the captured old image/container ID and source backup path for a deliberate follow-up recovery.
6. Always remove the transient `/tmp` archive after extraction (including a handled failure); never remove the retained source backup automatically.

### Technical verification

The script's successful verdict requires all of the following:

1. `docker compose build` and `up` exit successfully.
2. `am-service` is running after a bounded poll.
3. `curl -fsS http://localhost:18090/api/channels` returns parseable channel JSON from the new container. The report names registered adapter names/counts, not secrets or message bodies.
4. The messaging-service migrations in source are represented in `am-postgres`'s `__EFMigrationsHistory`, using a scoped `docker compose exec -T am-postgres psql` query whose output is migration IDs only. This preserves CARD-0265's migration evidence instead of treating a running process as proof that the schema advanced.
5. Recent `am-service` logs contain no startup exception. Limit the displayed log window and redact configuration values; the script must not dump arbitrary container logs into a task report.

The script cannot safely prove a live bidirectional Telegram/Slack round trip without sending a real external message. `docs/telegram-bot-ops.md` retains the human verification step: use only the `Antiphon-Family` test group, never the live Family group. A Deploy-role report must state whether that final check was performed or explicitly left to the operator.

## Documentation and delegation changes

1. Add the script with a complete comment-based help block: target, source-of-truth rule, dry-run and execution examples, exact production confirmation requirement, verification contract, rollback evidence, and no-secret logging rule. Keep it ASCII-only for Windows PowerShell 5.1 compatibility.
2. Replace the manually enumerated `tar`/`scp` block in `docs/telegram-bot-ops.md`'s **Deploying the messaging service (server2)** section with the script as the sole source-built deployment procedure. Retain the test-group warning and explain that the source manifest comes from the Dockerfile.
3. Keep `docs/messaging-standalone.md` and `docs/slack-bot-ops.md` as links to the Telegram ops procedure; do not copy a second remote recipe into either document.
4. Add a concise rule to the relevant deployment/orchestration reference: once `-Deploy` has explicit authorization, a Deploy-role delegate may run this script and report its final verdict; it may not reconstruct the SSH/tar/Compose sequence ad hoc.

## Tests and acceptance criteria

Do not add Pester solely for this script. Keep parsing and archive validation independently callable so a standard `pwsh` contract test can exercise them without contacting server2.

| Test | What it pins |
|---|---|
| Dockerfile parser fixtures | Current shell-form `COPY`, comments/continuations, several source paths, and final-stage `COPY --from` exclusion. |
| Unsafe-syntax fixtures | JSON form, variables, glob, absolute path, parent traversal, unknown option, and missing path each fail before archive/SCP. |
| Real Dockerfile contract | Every derived source exists under `src`; the generated archive contains each source and no `bin`/`obj`/`bin-` payload. This test does **not** repeat the current project list as an expected fixture. |
| Remote-command seam | Given fake SSH/SCP runners, asserts no write command is invoked without `-Deploy`, Compose output is narrowed before it reaches logs, and failures retain safe diagnostic/backup evidence. |
| Manual staging rehearsal | On server2, run dry-run/preflight first; then use a non-production staging directory or an explicitly approved deployment window to prove upload, replace, build, technical verify, and final verdict. |

The final implementation evidence must include the Dockerfile hash, derived-entry count, remote Compose context/Dockerfile projection, previous container/image identifier, retained backup path, migration comparison count, endpoint result, and the single final verdict. It must contain no token, resolved environment, or real conversation text.

## Scope result

CARD-0270 is scoped to `am-service` now. The school-revision server2 compose reported only image deployments and no `build` section; its versioned image comes from another delivery path. Local `scripts/deploy-local.ps1` is a separate AppHost restart/deploy contract and is intentionally not changed. A future remote source-build target should get its own target-specific script and Dockerfile parser contract first; only then is common extraction justified.

## Non-goals

- No remote deployment in this Plan pass.
- No attempt to move remote Compose secrets, `.env`, or server configuration into this repository.
- No generic SSH execution framework, arbitrary-target parameters, automatic rollback, or automatic real-message test.
- No manually maintained project/file list anywhere in the new recipe or its operator docs.

