# CARD-0487 / CARD-0545 / CARD-0599 Windmill definitions

Checked-in script and schedule payloads for:

- `u/lndcobra/antiphon_nightly_tests` — desktop tag, `0 30 0 * * *`, Europe/London, empty args. The
  wrapper's final stdout line is its JSON completion record, which Windmill stores as the job result
  (CARD-0545 D-10).
- `u/lndcobra/antiphon_nightly_readiness` — desktop tag, `0 */30 * * * *`, Europe/London, empty args. The
  Windows-side local readiness evaluator (`scripts/nightly-health.ps1`). It reads
  `C:\Antiphon\nightly\readiness-config.json` and folds the independent watchdog's snapshot into
  `last-monitor.json`. Running on the desktop is correct: if Windows is down the server that consumes
  readiness is down too, and if Windmill is down readiness goes stale and fails closed.

The former Windmill health-monitor definition was deleted by CARD-0545 (D-9). It was never registered,
could not run from a worker outside the Windows host (it depended on `host.docker.internal`), evaluated
everything on Windows, and notified through a Windmill job, so it was not independent of either failure
domain. Outage detection and notification now belong to the nightly watchdog, which runs outside
Windmill and outside the Windows host: see `docs/nightly-watchdog.md`.

- `u/lndcobra/antiphon_release_candidates` — desktop tag, `0 30 8,16 * * *`, Europe/London, empty
  args. **NOT REGISTERED and its schedule payload is `enabled: false`.** The lane tests the named
  `rc/YYYY-MM-DD` ref. The wrapper explicitly passes `-Ref rc/2026-10-18`; review and update
  this literal pin on a release-line rollover. It does not select a line from the current date
  or remote branch order. It does not cut `release/rc-<UTC stamp>` from `origin/master`. It runs
  `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>` against the
  candidate's own state root, and publishes a CalVer tag plus GitHub Release only on RC
  complete-green. RC green earns release credit only — it never writes the master attempt, green,
  monitor or Interim qualification receipt. Two cuts a day are attempts, not a guarantee of two
  releases. The scheduled lane is not qualified. Owner: `docs/release-gates.md`.

CARD-0599 D-7 also corrected trigger provenance: the master wrapper used to pass no args at all and
let the native `-Trigger` default to `scheduled`, including for manual invocations. Both wrappers now
derive the trigger from the runner's own `WM_SCHEDULE_PATH` matching the expected schedule path, and
otherwise pass `manual`. An API caller's trigger string cannot manufacture scheduled credit, and a
missing schedule context refuses scheduled credit rather than defaulting to it.

Registration is an operator step, and `scripts/register-release-gates.ps1` is its front door: preview
by default with zero writes, `-Apply` to register with readback, schedules always **created
disabled**, and `-EnableSchedule <nightly|readiness|rc>` (requires `-Apply`) to turn on exactly one.
It reads its Windmill token from an operator-placed file named by an untracked profile
(`release-gates-deploy.example.json` shows the shape) and never prints it. Do not infer live
registration from these files. Do not mint tokens here. Do not put credentials or watchdog host
details in these definitions.

Reduced dispatch verification stays **inactive** until
`docs/investigations/<date>-card-0487-nightly-qualification.md` exists and a current monitor
result is healthy.

See `docs/testing-and-build.md` (Nightly), `docs/nightly-watchdog.md` and `docs/release-gates.md`.

## Daily plain Postgres snapshot (operator 2026-11-10)

CARD-1405 planned `scripts/windmill/antiphon-postgres-snapshot.json`, the matching
schedule payload, and `scripts/register-postgres-snapshot.ps1`. Those files were
never committed. `git log --all --diff-filter=A` shows no addition of them. The
name `antiphon_postgres_snapshot` appears in the CARD-1401 plan (`719725f04`)
and not in a script commit. CARD-1405 commit `222610db8` added
`scripts/postgres-snapshot.ps1` only. CARD-1413 hardened that script. The card
closed as a script, with the schedule left disabled until a person enabled it.

`-Plain` on `scripts/postgres-snapshot.ps1` is off unless named. The age path is
unchanged and still fails closed without a recipient. `-Plain` writes a
compressed `pg_dump -Fc` file, with no age step, to
`C:\Users\lndco\antiphon-backups` (outside OneDrive, user-only ACL). It keeps 14
plain dumps and prunes only after `pg_restore --list` accepts the new file.
`snapshot-status.json` records utc, exit, bytes, a 12-character sha256 prefix,
duration seconds, table count, `mode=plain`, `encryption=none`, and
`operatorDecision=2026-11-10`. The operator asked for unencrypted compressed
dumps on 2026-11-10 because age and a recipient are not set up.

The scheduled job also copies each verified plain dump into
`C:\Users\lndco\OneDrive\Backups\antiphon-postgres`. The operator asked for
that copy on 2026-11-10. The dump is unencrypted. It syncs to the cloud, and
the database holds tokens and transcripts. The age path stays available and
is still the default when `-Plain` is not named. The script has no age key.
`-OneDriveCopy` is off unless it is named, so an ad-hoc run does not write
into OneDrive. The scheduled invoke script names `-OneDriveCopy`. The primary
destination stays outside OneDrive; a plain destination inside the sync root
still fails closed with `destination-inside-onedrive`. The OneDrive root and
its `Backups` folder must already exist. If either is missing, the status
field is `oneDrive=missing`, nothing is written in OneDrive, and the local
dump still stands. The copy is written as a name ending in `.partial` and
renamed only when its sha256 equals the local file. OneDrive keeps 7 dumps.
The local folder keeps 14. Prune runs only after the new file is in place
and hash-verified. `snapshot-status.json` adds `oneDrive` (`ok`, `missing`,
or `failed`), `oneDriveBytes`, and `oneDriveFolder`. The Telegram line ends
with `+cloud` when the copy succeeded, and with `no cloud copy` as a warning
when the copy failed or the folder is missing.

`-EnableSchedule` is that human enable. Preview writes nothing.
`-EnableSchedule` without `-Apply` refuses. The checked-in schedule payload is
`enabled: true` because the operator asked for the daily job.

`u/lndcobra/antiphon_postgres_snapshot` runs at `0 50 7 * * *` Europe/London
(07:50) on the desktop worker. It SSHes to
`C:\Users\lndco\antiphon-snapshot-runner\invoke-postgres-snapshot.ps1`. The
runner directory's `SOURCE-SHA` file is the sha256 of the
`postgres-snapshot.ps1` it should run. A candidate is used only when its
hash matches that pin, so an older checkout is not chosen ahead of the
pinned bytes. Staging is `C:\Users\lndco\antiphon-snapshot-staging`. The default
under LocalAppData\antiphon fails the parent check on this desktop because
that folder grants another account full control. The profile directory's
parents pass, and the CARD-1413 check is unchanged.

`u/lndcobra/antiphon_postgres_snapshot_stale` runs at `0 0 8 * * *`
Europe/London. It posts only when no plain dump and no exit-0 status is newer
than 26 hours. A healthy 07:50 run does not send a second message.

### Status line

`scripts/windmill/job-status.ps1` and `job-status.schema.json`. A job writes one
JSON object with `job`, `icon`, and `status` (`ok`, `warn`, `fail`, `stale`)
and an optional `detail`. The renderer prints one line, for example
`DB backup: OK 0.6 GB`, `DB backup: FAIL exit 3`, or `DB backup: STALE`.
This job writes `C:\Users\lndco\antiphon-backups\job-status\db-backup.json`.
Tests call the renderer only. The live post uses `scripts/tg-send.cs` on the
messaging bus, to Mike's DM, and only the invoke script does that.

### How jobs notify today

These jobs are not changed here.

- `u/lndcobra/antiphon_nightly_tests` stores a JSON result. Telegram for that
  lane is the watchdog outside Windmill.
- `u/lndcobra/antiphon_nightly_readiness` writes the local monitor file. It
  does not post to Telegram.
- `u/lndcobra/antiphon_release_candidates` is not registered. Its payload stays
  disabled.
- `u/lndcobra/telegram_notify` is the shared Windmill DM helper.
- `u/lndcobra/desktop_heartbeat` runs at 08:00 and proves the desktop worker.
- `u/lndcobra/antiphon_build_junk_cleanup` is weekly and does not post from the
  script in this repo.
- `u/lndcobra/browser_overnight_close` runs at 04:00 and does not post from the
  script in this repo.

### Later digest

An 08:00 digest can read each `job-status/*.json` file and post one message of
`Format-JobStatusLine` rows. Until that exists, this backup posts its own line
at 07:50. The 08:00 stale check stays the second signal and stays quiet when a
success is newer than 26 hours.
