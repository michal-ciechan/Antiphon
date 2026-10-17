# CARD-1305 RC environment and lane-placement defects D-2..D-6

Confirmed on `7b3b9adbe98b18ae306b73cfb220331dc433f801`. These are five
environment and lane-placement mechanisms, not one product regression.
`origin/rc/2026-10-15` is `4ebdd47ded247558f72e2af8c740130f0b8f5513`, six
commits behind this tip and an ancestor of it and of `origin/master`
`7804e6efea1d767b2647ca4b9d8363903ef4a1da`. The defect blobs match that rc
tip. They match master except `tests/test-execution-policy.json`, which has
seven extra exclusion classes and still omits the classes below.

Stored RC observations are in `docs/investigations/2026-10-15-release-candidate-defects.md`
on `origin/feat/card-task-1c14bf28` (`84fb9b546bc2966159feb8812fab426c205be668`),
candidate `067b47e2dc903247f1a7c8aa539032cc4266e4ad`. That note is not on
this tip. No whole lane was re-run. The listener failure, the absent Postgres
name, the missing library, and the unset canary flags were measured here.

## D-2 — listener enumeration is the Windows cmdlet

`verify-dev-stack.ps1:24` sets `ErrorActionPreference = "Stop"`.
`verify-dev-stack.ps1:79` is still:

```powershell
$listening = (Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue).LocalPort | Sort-Object -Unique
```

Blame `b7e17830250947ebee43e2da63db0a5302bb283b`. The same line is on rc and
master. On this host the command is absent. The same statement, with
`ErrorActionPreference = "Stop"`, exited 1:

```text
System.Management.Automation.CommandNotFoundException
The term 'Get-NetTCPConnection' is not recognized as a name of a cmdlet, function, script file, or executable program.
```

`-ErrorAction SilentlyContinue` does not bind, because the command is never
resolved. The throw is before the HTTP checks at `verify-dev-stack.ps1:98`.
`/usr/bin/ss` exists and `ss -ltn` exited 0. `lsof` is absent. No test
mentions `verify-dev-stack` or `Get-NetTCPConnection`.

The full script was not executed. After the Docker check it creates a
network (`verify-dev-stack.ps1:62`).

## D-3 — smoke requires the desktop Postgres container

`verify-dev-stack.ps1:70-74` records `Postgres container` from
`docker ps --filter name=antiphon-postgres`, and only when Docker answered
and `-SimpleMode` is off. The name is `docker-compose.dev.yml` container
`antiphon-postgres` (compose file line 26). `docs/bootstrap.md:563` describes
that container as the Docker Desktop always-on database. Here
`docker ps -a --filter name=antiphon-postgres` exited 0 and printed no row.
`Add-Result` does not throw, so this FAIL is recorded and then D-2 aborts
the script. Blame of the check is `15b0c735d7` (2026-06-10). No product
commit removes the container from this host; the host is not that desktop
stack.

## D-4 — 69 land-delivery cases fail closed on non-Windows

`tests/Antiphon.E2E/Fixtures/LandDeliveryFixture.cs:59-61`, blame
`96b5f4d9c5b01f3312b07b9e5dc519ffceda8b49`:

```csharp
OperatingSystem.IsWindows().ShouldBeTrue();
ConPtyRedistributable.TryLocate(out _, out var why).ShouldBeTrue(why);
File.Exists(... "fakegrok.exe").ShouldBeTrue("native FakeGrok must be staged");
```

Line 59 throws `ShouldAssertException` before the ConPTY and `fakegrok.exe`
lines. The stored RC error was `OperatingSystem.IsWindows()` should be
`True` but was `False`. The current inventory that reaches `InitializeAsync`
is still 69 cases: 52 in `AgentTaskLandDeliveryE2ETests`, 12 in
`DispatchBaseWarningDeliveryE2ETests` (five `async Task` methods plus seven
`public Task` methods that enter through `CrashAsync` at line 115), and 5
in `WorktreeRetirementDeliveryE2ETests`. All three classes are
`[Category("OptIn")]` (`AgentTaskLandDeliveryE2ETests.cs:14`,
`DispatchBaseWarningDeliveryE2ETests.cs:13`,
`WorktreeRetirementDeliveryE2ETests.cs:15`).

`docs/release-gates.md:87` defines `rc` as the automated Windows profile.
`scripts/lib/nightly-policy.ps1:408-412` says OptIn alone is not exclusion.
None of the three classes appears in `profiles.rc.exclusions` on this tip,
on `origin/rc/2026-10-15`, or on `origin/master`. A Linux `rc` run therefore
executes them, and they fail the admission assert. `LandDeliveryFixtureProbeTests`
does not call `InitializeAsync` and is not part of the 69.

## D-5 — the runner image does not install Chromium's library

`tests/Antiphon.E2E/Fixtures/PlaywrightFixture.cs:38-41` launches headless
Chromium. Blame `9b64185cc6`. The stored hook error was
`libglib-2.0.so.0: cannot open shared object file`. On this host
`libglib-2.0.so.0` is absent. `docker/session-runner-grok/Dockerfile:98`
installs `curl ca-certificates bash libcurl3-gnutls libexpat1 zlib1g procps`
and nothing else in that layer. The same line is on rc and master. No
Playwright OS-dependency package is in that image. The 35 stored browser
cases were not re-executed.

## D-6 — three required F-5 canaries skip

`ChannelOutboundIsolatedTests` is `[Category("Headed")]` and
`[Category("OptIn")]` (`ChannelOutboundIsolatedTests.cs:12-13`). All three
tests call `RequireOptIn()` before any other check:
`Static_logger_reaches_owned_host_file_after_restart` at line 21,
`FakeGrok_outbound_mode_reads_frozen_request_and_invokes_a_tool` at line 57,
and `Four_sources_convert_only_for_the_selected_conversation` at
`ChannelOutboundIsolatedTests.Composed.cs:38`. The guard at
`ChannelOutboundIsolatedTests.cs:132-137`, blame `e28d079bc78597dd74e3cdc98a349329e9a616c2`,
throws `SkipTestException("F-5 requires all three isolated opt-ins.")` unless
`ANTIPHON_HEADED_TESTS`, `ANTIPHON_BROKER_TESTS`, and
`ANTIPHON_CHANNEL_OUTBOUND_ISOLATED` are all `1`. All three are unset here.
The Linux-only skips at lines 58 and `Composed.cs:39` are not reached.

The class is not an `rc` exclusion on this tip or on master.
`docs/release-gates.md:96-99` and `:122-124` keep headed and live broker
inputs cleared in automated E2E, and say OptIn is not an exclusion.
`scripts/lib/nightly-coverage.ps1:733-741` marks coverage incomplete when
any terminal row is `Skipped` (`$skipped.Count -gt 0`). A stale
`policyHash` is refused at `scripts/lib/nightly-policy.ps1:88-91`. This
skip fails the automated profile on Windows as well as on Linux. The
current hash is `922cce2a0064bebfc90e211fe33db10dc74cb61ecb5d9f050bd00ec669e10ada`
(`tests/test-execution-policy.json:3`).

## Smallest fix

D-2 is the smallest code change. When `Get-Command Get-NetTCPConnection`
is absent, list listening ports from `ss -ltn`. Keep the cmdlet where it
exists. Do not call `lsof`. The red-first witness does not exist yet: one
new test opens a loopback listener and expects that port back. Today that
probe throws the `CommandNotFoundException` above. Commit the test red,
then the fallback.

Filter: `/*/*/VerifyDevStackSmokeTests/Linux_listener_probe_lists_ports_without_GetNetTCPConnection`

D-3 stays a check for the Windows desktop container. Do not create
`antiphon-postgres` on the runner. D-4 stays required on the Windows `rc`
lane. Do not skip those 69 cases on Linux inside the profile. D-5 is an
operator image change through the staged rollout, adding the Chromium OS
libraries to `docker/session-runner-grok/Dockerfile`. D-6 is one
`profiles.rc.exclusions` row for `ChannelOutboundIsolatedTests` with a
reason and an owner, then a recomputed `policyHash`. Do not set the three
opt-in variables in the automated lane. Master's extra exclusion rows have
to be kept when that policy change merges forward.

## Remaining uncertainties

The 69 and 35 historical cases were not executed again. Their failing lines
are unchanged, this host is not Windows, and `libglib-2.0.so.0` is still
absent. The TRX files cited by the 2026-10-15 note were not opened. Windows
presence of `Get-NetTCPConnection` was not measured.

## Not done, noted

No product, test, policy, or image change was made. The D-2 fallback and
its red test were not added.

## D-4 repair — the 69 cases stay on the Windows rc lane

Code at `4e261b2faeafa50f25d04e0087777c7c6a7f2fa2` added
`profiles.rc.exclusions` rows for `AgentTaskLandDeliveryE2ETests`,
`DispatchBaseWarningDeliveryE2ETests`, and
`WorktreeRetirementDeliveryE2ETests`. That list is the only rc exclusion
list. The exclusion schema has no platform field, and there is no separate
Linux rc profile, so those rows removed the 69 land-delivery cases from the
Windows rc lane. An rc complete-green could be earned without them. That
contradicts the smallest-fix note above, the profile description ("nothing
else may vanish from the census"), and `docs/release-gates.md` (rc is the
automated Windows profile).

Follow-up `e6796d56` deletes those three rows and recomputes `policyHash`
to `9d58816258ac606910dd5b5fb8dc09e3cb69c06cafffa498963b6e7531610a7a`.
`ChannelOutboundIsolatedTests` (D-6) stays. The D-2 `ss -ltn` fallback
stays. The 69 cases belong on the Windows rc lane. A Linux placement needs
platform-scoped exclusion tooling, owned by a separate Backlog card.
