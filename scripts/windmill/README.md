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
  args. **NOT REGISTERED and its schedule payload is `enabled: false`.** CARD-0599's RC lane:
  `scripts/release-cut.ps1` cuts `release/rc-<UTC stamp>` from origin/master, runs
  `nightly-run.ps1 -Profile rc -Trigger rc -Ref <candidate> -ExpectedSha <sha>` against the
  candidate's own state root, and publishes a CalVer tag plus GitHub Release only on RC
  complete-green. RC green earns release credit only — it never writes the master attempt, green,
  monitor or Interim qualification receipt. Two cuts a day are attempts, not a guarantee of two
  releases. Owner: `docs/release-gates.md`.

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
