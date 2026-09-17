# Nightly watchdog deployment assets (CARD-0545)

Tracked, host-agnostic assets for the independent nightly watchdog. The owner document is
`docs/nightly-watchdog.md`; read it first.

| File | Purpose |
|---|---|
| `antiphon-nightly-watchdog.service.template` | systemd system unit. `@@SERVICE_USER@@` and `@@REMOTE_ROOT@@` are rendered from the deploy profile. |
| `deploy.example.json` | Deploy profile shape with placeholders only. Copy it to an untracked location and fill it in. |
| `env.example` | Names of the keys in `<remoteRoot>/env` on the watchdog host. No values. |
| `qual.example.json` | Qualification-instance config template (`--config ./qual.json`, namespace `mc/qual`). |

Rules:

- No tracked file names the watchdog host, its addresses, its user or an absolute home path. Host
  details live only in the untracked deploy profile and in the host's own env file.
  `scripts/test-deploy-nightly-watchdog.ps1` (`Test-C545_HostAgnosticAssets`) enforces this.
- The deploy profile is looked up in this order: `-Profile <path>`, `ANTIPHON_WATCHDOG_DEPLOY_PROFILE`,
  `C:\Antiphon\nightly\watchdog-deploy.json`, then exactly one `scripts/nightly-watchdog/*.local.json`
  (gitignored). No profile, or a profile still holding `<placeholder>` values, exits 3 and contacts nothing.
- `scripts/deploy-nightly-watchdog.ps1` without `-Deploy` is a read-only preflight (publish, checksum,
  render, remote `--self-check` dry run). `-Deploy` is the explicit opt-in that uploads, appends only the
  absent non-secret env keys it owns, installs and enables the unit and verifies the snapshot URL.
- Secrets (Windmill token, bot token, destination chat id, reader api id/hash, reader session) are placed by
  the operator on the host. The deploy script never reads, writes or prints them.
