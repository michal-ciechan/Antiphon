# task eb43fc0e: land graph re-record on master, then cut rc/2026-11-03

Outcome: the digest was re-recorded on master and rc/2026-11-03 was cut from it. Both worked.

## SHAs and ls-remote proofs

- Master before the update: `59a37c1675862a7ccc8bc7bc36769e2ac50bc0b8`. The pin was confirmed with ls-remote before starting.
- Update commit, which is also `$cut`: `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`, subject `chore: land graph digest for the csproj edits since 907edaebb`.
- Master after the update: `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd refs/heads/master`. It was a plain fast-forward push, `59a37c167..919d8d42f`, read back with ls-remote.
- rc line: `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd refs/heads/rc/2026-11-03`. It was a plain new-branch push, read back with ls-remote, and equals `$cut` exactly.
- Candidate id: **rc-2026-11-03-919d8d42f14d**.

## I.1: -Check before the update

The evaluator tool tree id is `642837e6415f572aee355c73c95298ec0e2483b5`, the same as the brief's. I ran the cached evaluator `/work/cache/land-graph-audit/642837e6415f572aee355c73c95298ec0e2483b5/Antiphon.LandGraphAudit.dll` via `-ToolPath`. Nothing was built.

```
digest=266595CBAA5CA00BE0182F33804AE94AD851DB9BBCF74C3FAAF211D5D5A49CFA count=69
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

Exit 1, after 9.9 s. The pre-check's manifest/digest mismatch was the only refusal. There was no pin refusal and no other refusal.

## I.2: build-definition changes, 907edaebb..59a37c167

I searched `*.csproj *.props *.targets Directory.* *.sln global.json`. Only one commit touched them: `de27ce2fd test(timing): OpenTelemetry-style test traces with product spans`. The test(db) commits (74178698e, 59a37c167) and the phone-home commit (135865831) touched none of these files. Each of the four files gained one line, a `Compile` item add:

- `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj`: `+<Compile Include="../Shared/TestTrace.cs" Link="TestHelpers/TestTrace.cs" />`
- `tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj`: `+<Compile Include="..\Shared\TestTrace.cs" Link="TestSupport\TestTrace.cs" />`
- `tests/Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj`: `+<Compile Include="../Shared/TestTrace.cs" Link="TestTrace.cs" />`
- `tests/Antiphon.Tests/Antiphon.Tests.csproj`: `+<Compile Include="..\Shared\TestTrace.cs" Link="TestHelpers\TestTrace.cs" />`

None of these edits touches a pinned target body. The commits since 907edaebb also leave `tools/Antiphon.LandGraphAudit` unchanged, which is why the tool tree id still matches.

## I.3: the single authorised -Update

Every condition held: the mismatch was the only refusal, count stayed 69, and the changes were item adds only. The `-Update` printed `digest=266595CB... count=69`, five `not-built:` lines, `canary=...`, `phase=pass2` and `approved`. It exited 0 after 27 s.

The pins file is unchanged; its blob is `97151190360cc5515e70ae743c2858e96151382e` before and after. The full diff:

```
--- a/server/Application/Services/LandVerificationBuildGraph.evaluation.txt
+++ b/server/Application/Services/LandVerificationBuildGraph.evaluation.txt
@@ -1,3 +1,3 @@
-7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22
+266595CBAA5CA00BE0182F33804AE94AD851DB9BBCF74C3FAAF211D5D5A49CFA
 10.0.403
 97151190360cc5515e70ae743c2858e96151382e
--- a/server/Application/Services/LandVerificationBuildGraph.manifest.txt
+++ b/server/Application/Services/LandVerificationBuildGraph.manifest.txt
@@ -1,2 +1,2 @@
-7456743D28E951E9A6FBF53874B166750CD2631588969556A891FCC2CA673C22
+266595CBAA5CA00BE0182F33804AE94AD851DB9BBCF74C3FAAF211D5D5A49CFA
 69
```

-Check after the update ran twice:

- Without `tests/Antiphon.E2E/TestOutput/Logs/run.log`: same output as the `-Update`, ending `approved`, exit 0.
- With that run.log present: `approved` again, exit 0.

The canary directories were removed by the script. I deleted the run.log and its two directories afterwards by their literal paths.

## I.4: verification

- **Solution build:** one lease (slot wait 0 s, held 241 s): `dotnet build Antiphon.sln -p:UseAppHost=false`. Wall time 3:59.8, **0 errors**, 1678 warnings, **no NETSDK1047**.
- **Smoke run 1 (`--no-build`, one lease, run.log present):** 3/3 passed in 2m25s: `C1385_RegistryMatchesNarrowGraph`, `C1483_S4_RealRepositoryApproves` and `C1483_S5_CheckRequiresEvaluationRecord`. The TRX names all three.
- **Smoke run 2 (`--no-build`, one lease):** the LandGraphEvaluationIgnoredItemTests set passed 9/9 in 15 s. The TRX names all 9 cases.
  - The file `LandGraphEvaluationIgnoredItemTests.cs` declares `partial class LandGraphEvaluationTests`, so the filter selects by method: `/*/*/(LandGraphEvaluationTests*)/(C1385_Ignored*)|(C1385_CopiedFileMissingFromCanaryPassRefuses*)`.
- Nothing else ran.

All bin/obj directories were deleted by literal path. The worktree is clean.

## A1: release-line checks

- Before the push, `git ls-remote origin refs/heads/rc/2026-11-03` printed nothing.
- `git cherry 919d8d42f origin/rc/2026-11-01` (rc/2026-11-01 tip `10a826b9d61f2c2cdfa29a43cac59fe360c99ea9`) gave **0 `+` lines** and 24 `-` lines. rc/2026-11-01 has no commit that master lacks.

## Findings (read-only)

### (i) Baked Postgres image absent, for example on the Windows desktop with Docker Desktop

The fixture **falls back. It does not fail.**

1. **Lookup.** `BakedImageExistsAsync` returns false when the image is not found. Any other Docker error writes one stderr line `C1420 pg-image lookup failed` and also returns false.
2. **Normal path.** The bootstrap then takes the path it took before these commits:
   - It starts a container from `postgres:16-alpine`. The image is pulled from Docker Hub only if it is not already present, as before.
   - It restores the schema from the cache (key `c1420-v2`) or migrates (about 12 s).
3. **Background bake.** After that it calls `BakeInBackground`, which:
   - starts a second `postgres:16-alpine` container with `PGDATA=/pgdata`
   - copies in the captured SQL, restores it with psql and creates the protected template
   - stops the container and runs `docker commit` to `antiphon-test-pg:<key>`
   - prunes to the newest 4 tags

   This is entirely local: no registry, no push, and no network beyond the base image.
4. **Locking and teardown.** One baker runs per key per host, guarded by a lock file in `%TEMP%\antiphon-test-pg-bake`. Teardown waits up to 2 min for the bake. After that it abandons the bake with a stderr line. A bake error never fails the run; it writes one `C1420 pg-image bake failed` line.
5. **Duration.** Not measured on Windows. It should be about one schema restore (4–7 s by the commit's numbers) plus a container start, stop and commit, overlapping the tests. Later processes with the same key start from the image. The key changes only when migrations change (`TestMigrationFingerprint`).
6. **Switches.** `ANTIPHON_TEST_PG_IMAGE=off` disables both lookup and baking.
7. **Ryuk-free cleanup.** Containers carry the owner label `MachineName/pid/start-ticks` and are removed explicitly at teardown. A process that is killed, such as an aborted qualification run, leaves its container until the next test process's bootstrap sweep removes it. `ANTIPHON_TEST_PG_RYUK=on` restores the reaper.
8. **Disk cost.** Docker Desktop will keep up to 4 baked tags.
9. **server2.** Several task processes share one runner container, with one hostname and one PID namespace, and the liveness check works there. I saw a peer task's live container correctly labelled. Other runner containers have different hostnames, so the sweep treats their containers as foreign and does not touch them.

### (ii) Product defaults from 135865831

**No change to the Linux server2 runner.**

- `PhoneHome:Executables` defaults to an empty dictionary, and no tracked json, yml or env file sets it.
- With the map empty, codex is still pinned to the image path, and `WithConfiguredExecutable` does nothing.
- The validators only widen: they now also accept `X:/` and `X:\` paths. POSIX paths behave as before, and ChildCodexHome still refuses `/`, `/tmp` and `..`.
- POSIX Raw allow-list entries are still compared ordinally.
- The POSIX branch of `RemoteVerificationWorkspace.RequirePrefix` is unchanged.

### (iii) Startup scripts

**None.**

- `scripts/start-windows-phone-home-runner.ps1` is referenced only by `docs/docker-stack.md` and its own header. It is a manual foreground script: it refuses on non-Windows and refuses the RunnerId `local`/`desktop`.
- `scripts/summarize-test-trace.ps1`, added in de27ce2fd, is also manual.
- No AppHost, restart, dev-aspire or scheduled-task file references either script.

### (iv) Test traces (TestTrace, TestTiming)

- **Default state:** off. The JSONL file exporter installs only when `ANTIPHON_TEST_TRACE_FILE` is set. Only the docs and the summarize script mention that variable; run-checkpoint and nightly do not set it.
- **Extra files in a run's log directory:** none by default.
- **TUnit's HTML report:** TUnit 1.44 already writes `<results>/Antiphon.Tests-linux-net9.0-report.html` beside the TRX; I saw it in both smoke runs. It now nests up to 100 external spans. The TUnit version did not change, so that file is not new.
- **Production:** `Program.cs` adds only the LiveTiming source, so the new `Antiphon.Server` spans cost one null check.
- **Slowdown:**
  - In test hosts, while TUnit's listener samples, one Activity is created per phase, git command or dispatcher stage.
  - Not measured. I expect it to be negligible.
  - With the trace file set, each span adds a JSON serialisation, and the file is uncapped.
