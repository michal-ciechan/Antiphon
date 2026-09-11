# CARD-0487 Windmill definitions

Checked-in script and schedule payloads for:

- `u/lndcobra/antiphon_nightly_tests` — desktop tag, `0 30 0 * * *`, Europe/London, empty args.
- `u/lndcobra/antiphon_nightly_health` — server2/default tag, every 30 minutes. Must not use `desktop`.

Registration is an operator step. Do not infer live registration from these files. Do not mint
tokens here. Do not put credentials in these definitions.

Reduced dispatch verification stays **inactive** until
`docs/investigations/<date>-card-0487-nightly-qualification.md` exists and a current monitor
result is healthy.

See `docs/testing-and-build.md` (Nightly).
