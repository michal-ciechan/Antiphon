# CARD-1744 S4 review amendment (D-14, D-15)

Amends [2026-11-19-card-1744-s4-s5-plan.md](2026-11-19-card-1744-s4-s5-plan.md) after the S4 round 1
review (`docs/superpowers/reviews/2026-11-19-card-1744-s4-review.md`, Review task ea0373d4, blockers
D-1 and D-2). This is a separate file because master carries the plan through its own commit, and a
branch-side edit of that plan is an add/add conflict. Where this file and the plan disagree, this file
wins.

## D-14 (autostart behaviour), added cases

`autostart-session-runner.ps1` fails closed. Each case below appends
`[autostart] phone-home config refused: <reason>` to `logs/session-runner.log`, exits 2, and never
reaches `run-daemon.ps1`. There is no fallback to the direct runner.

| Reason | Cause |
|---|---|
| `helper-load-failed` | `scripts/lib/windows-phone-home-env.ps1` is missing or fails to dot-source (for example a Windows PowerShell 5.1 parse error). This refuses whether or not the config file exists, because without the helper even the direct plan cannot be built. |
| `config-unreadable` | The config path exists but cannot be read or parsed (an access error, a sharing violation, or a directory with that name). |
| `plan-resolve-failed` | `Resolve-SessionRunnerAutostartPlan` throws. |
| `plan-mode-invalid` | The plan is null, or its mode is not exactly (case-sensitive) `direct` or `phone-home`. |

A refused config (bad schema) keeps its own reason, as before.

Witnesses: `C1744G3PhoneHomeSupervisionTests` runs the real autostart script, copied with the helper
into a scratch repo, against a stub `run-daemon.ps1` that records its arguments. The cases are
`autostart-unreadable`, `autostart-helper-load`, `autostart-refused-script` (each: exit 2, the log line,
stub never invoked) and `autostart-script-direct`, the positive control (no config: exit 0, the stub
receives `--urls http://localhost:17204`, no refusal line). CP-11's floor rises to 29.

## D-15 (AppHost launch guard), corrected recovery text

The refusal printed by `Get-WindowsPhoneHomeLaunchRefusal` (and so by `restart-apphost.ps1` and
`dev-aspire.ps1`) is conditional. Replace the D-15 sentence "followed by the recovery: delete the file,
run `restart-session-runner.ps1 -Hard`, then retry" with:

> followed by the conditional recovery: if the Linux server is serving, this refusal is correct and
> the file must not be deleted. Only during rollback, after the Linux server is stopped and
> `pg_stat_activity` is clear: delete the file, run `restart-session-runner.ps1 -Hard`, then retry.

The exact text is:

```
REFUSED: phone-home-config-present
If the Linux server is serving, this refusal is correct: do NOT delete the file. Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear: delete C:\Antiphon\runner\phone-home.json, run restart-session-runner.ps1 -Hard, then retry.
```

`apphost-guard.present` pins the condition sentences, the path, `restart-session-runner.ps1 -Hard`, and
the absence of `restart-linux-server` (T-21). The S5a docs fix task 87851070 carries the runbook side
of the same condition (`docs/apphost-runbook.md` exit 3 row). The plan's RB row recovery cell is
already inside the rollback, after `docker compose stop` and the `pg_stat_activity` check, so it
stands.
