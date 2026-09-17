# CARD-0487 / CARD-0545 Windmill definitions

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

Registration is an operator step. Do not infer live registration from these files. Do not mint
tokens here. Do not put credentials or watchdog host details in these definitions.

Reduced dispatch verification stays **inactive** until
`docs/investigations/<date>-card-0487-nightly-qualification.md` exists and a current monitor
result is healthy.

See `docs/testing-and-build.md` (Nightly) and `docs/nightly-watchdog.md`.
