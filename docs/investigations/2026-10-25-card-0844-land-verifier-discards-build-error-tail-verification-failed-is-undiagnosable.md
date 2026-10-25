# CARD-0844: Land verifier discards build error tail; verification_failed is undiagnosable

Investigation 29105c98, 2026-10-25. Both refused lands failed their desktop Windows **build**, before any test execution. The exact build errors cannot be recovered from the retained verifier evidence. Error-tail loss is confirmed and already tracked by Backlog CARD-0844; the underlying build failures remain unconfirmed.

## Boundaries and provenance

Read-only incident inspection; only this report was authored. No build, test, land, restart, process stop, qualification/native-lock operation, build-slot acquisition, source repair or tracker write was performed. Live qualification `db2c2088` and supervisor 236672 were untouched. No sub-delegates were used.

Repository inspection tip: `9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9`. `GET /api/version` returned running server SHA `a13e544e2baea214376944129a66ec4b525fafce`, capability `land-v2`. The verifier, land-service and verification-policy files cited below have no diff between that running SHA and the inspection tip. Observations were retrieved on 2026-10-25 around 23:53-23:57 UTC, using task-authenticated GETs and read-only SQL through the documented desktop Postgres container.

## What verification actually runs

- `server/Program.cs:500` registers `ILandingVerifier` as the local `LandingVerifier`. `server/Infrastructure/Git/LandingVerifier.cs:24` calls the land-service driver in this server process; it does not send the build to the task's server2 runner.
- `server/Application/Services/LandVerificationPolicy.cs:11` requires a build for force-build, a selected test filter, a non-master destination or any non-documentation diff. These two requests recorded `diff-requires-build`. Test/fixture-only C# changes do not qualify for the documentation skip.
- `server/Application/Services/AgentTaskLandService.cs:1933` constructs `dotnet build --artifacts-path <generation> --configuration Debug -maxcpucount:4`, in the detached land worktree. There is no project argument, so this is the root solution build (`Antiphon.sln` exists there), not a build of only changed classes.
- At `server/Application/Services/AgentTaskLandService.cs:1944`, a nonzero build returns failure. At line 1945 a null/blank filter returns success immediately after a passing build. **No default test selection exists:** neither changed tests, a fixed smoke nor the full suite is executed without a filter.
- Only a nonempty `-Verify <treenode-filter>` adds `dotnet run --project tests/Antiphon.Tests --no-build --artifacts-path <same-generation> --configuration Debug -- --treenode-filter <filter> --report-trx ...` (line 1948). A fresh TRX with nonzero executed tests, all passed and zero failed is required (lines 1952-1962). The build remains mandatory.
- `scripts/delegate.ps1:301` documents that the full suite is deliberately not a land gate; line 840 sends `verify` only when supplied. `docs/orchestration-loop.md:2256` describes the mandatory-build cases and narrow documentation skip. The stored `VerificationCommand` is a generic description containing both build and run, not proof that tests ran.

## Durable operation evidence

SQL source: desktop `antiphon-postgres`, database `antiphon`, table `AgentTaskLandings`. The rows below have `VerificationFilter IS NULL = true`. All target `refs/heads/master`, all used `C:\Antiphon\worktrees\land\0f9784a67773`, all prepared against `151ce388f663d130f90c4aa32d44d4d87f8d87c1`.

| Task | Request / operation | Original source | Rebased candidate | Outcome |
|---|---|---|---|---|
| 2a61a460 | `a441cd1f-1de9-4766-a274-3bf8a32cc8e8` / `48065bb8-3937-4733-910d-263942781828` | `b9e636a530d882a412a1ec93460c052338a65723` | `d85ebbc9741f1f1cdba9a863d960e19f6c80726c` | VerificationPassed=false; verified SHA/time null; verification_failed |
| 91ea9029 | `f2d56382-6a51-4c77-845e-69e0e8de776f` / `5baf79ff-74c9-4f09-8768-ee2a5df99c99` | `8c0bb33a554070ec2f1498206f91fd876ff3350e` | `efd3a13f56ed1182ee01a62adeb482fb4ea42cdb` | VerificationPassed=false; verified SHA/time null; verification_failed |
| 44298e2a | `1960b4e5-df3d-4882-9ab7-1fc53f52b03d` / `5bf2514c-e96a-4d36-80ac-63620dd4a991` | `5bfe1915af8cf720038ad002d11a60b7e50f1a65` | `9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9` | VerificationPassed=true; published and remotely confirmed at 23:46:44.716347Z |

`GET /api/agent-tasks/2a61a460` and `/91ea9029` corroborate publication Refused, cleanup NotStarted and reason verification_failed. Their terminal LandRefused events are 23:29:13.368912Z and 23:36:29.878492Z. The successful comparison actually ran **after both refusals**, not between them; 5bfe1915 is its source, while 9a3e6c7a is its published rebased SHA.

`StageOutcomes` rows `2ea63c0d-5076-4b86-b474-6657a667f645` and `2d105e97-dcbe-471f-9ac5-4a2df4fe8bdd` both say exactly `build failed or selected tests failed`; neither contains an error. The successful Verify row `2a7ab38d-da07-4c1b-9793-0f4872bcb461` says `build OK; selected verification passed`, despite its null filter. These are generic summaries, not test receipts.

### Server log and retained generations

Authoritative desktop log: `C:\src\Antiphon\server\logs\antiphon-20261025.log` (location owned by `docs/logs.md:11`). Line citations below refer to that file.

| Operation | Decision line | Slot grant | Joined child | Release |
|---|---:|---|---|---|
| 48065bb8 | 141612 | 141618, 23:22:49.473Z, waited=0s, maxcpucount=4, lease 080ddb84-c5c1-48db-9785-9c9a4c7f1c92 | 141883, PID 291100, start ticks 639285673696159066, 23:29:07.628Z | 141885, held=378s |
| 5baf79ff | 141903 | 141908, 23:29:46.808Z, waited=0s, maxcpucount=4, lease 76b51e99-71e0-4665-91ee-8733bb39af19 | 142021, PID 213536, start ticks 639285677870414563, 23:36:22.818Z | 142023, held=396s |
| 5bf2514c | 142066 | 142076, 23:36:55.622Z, waited=0s, maxcpucount=4 | 142281, PID 43056, 23:46:21.851Z | 142283, held=566s |

Unedited join evidence for the failed operations:

```text
2026-10-25 23:29:07.628 +00:00 [INF] Landing verifier joined task "2a61a460-3147-4dd1-8876-1ced31d9f5d9" operation "48065bb8-3937-4733-910d-263942781828" request "a441cd1f-1de9-4766-a274-3bf8a32cc8e8" child 291100 start 639285673696159066; exit and streams drained {"SourceContext":"Antiphon.Server.Infrastructure.Git.LandingVerifier"}
2026-10-25 23:36:22.818 +00:00 [INF] Landing verifier joined task "91ea9029-ab53-4b50-ba93-16edef0cf07f" operation "5baf79ff-74c9-4f09-8768-ee2a5df99c99" request "f2d56382-6a51-4c77-845e-69e0e8de776f" child 213536 start 639285677870414563; exit and streams drained {"SourceContext":"Antiphon.Server.Infrastructure.Git.LandingVerifier"}
```

Artifact parent (absolute):

```text
C:\src\Antiphon\.git\antiphon\land-verify\v1\dcaeb6b99f12dabcd4cc699633b466d1be3aeb7a7ffbc5a241b542ce89627fee\
```

- `25df0afb30ff410a895cb21db5e63758\`: warm generation used by 48065bb8 (log 141616); `state` is `building`, then quarantined (141905). Its Antiphon.Tests.dll timestamp is 23:20:00.7483172Z, **before this build**, so that leftover binary is not evidence of candidate compilation.
- `cfd9988b8dc548ecbc96fefe089cf1b7\`: new generation used by 5baf79ff (141906); `state` is `building`, then quarantined (142073). No `bin\Antiphon.Tests\...\Antiphon.Tests.dll` was found. This is consistent with failure in/around that project's build but cannot identify the error or rule out another failing target.
- `e1b09535545844d488f41adcf05e83b7\`: new generation for the successful comparison (142074); `state` is `ready`; its Antiphon.Tests.dll timestamp is 23:46:01.8178351Z.

All three `owner.json` files bind `c:\src\antiphon\.git` and toolchain `10.0.300|Microsoft Windows 10.0.19045|X64|Debug`. No .log, .binlog or .trx file was found in the inspected generation tree. The retained payload is bin/obj plus ownership/state markers, not compiler-output evidence. The shared land worktree has since moved on; do not run or reset it to reconstruct these candidates.

## Confirmed mechanism and attribution limits

The diagnostic-loss mechanism is reconstructed directly from the running source:

1. `AgentTaskLandService.cs:1979` redirects and reads child stdout/stderr into memory; line 1997 reduces the numeric exit status to `ExitCode == 0`.
2. Line 1944 makes a build-failure object containing `Tail(build)` (bounded at 1800 characters, line 2000).
3. `LandingVerifier.cs:34` converts that object to `new(result.Ok, result.Ok ? result.Description : result.Step + " failed")`, dropping the tail. Its observer logs slot/artifact/join events, not compiler streams (lines 50-60).
4. `AgentTaskLandingProtocol.cs:300` checks only Passed and refuses `verification_failed`; `AgentTaskLandService.cs:785` stores the generic StageOutcomes text quoted above. No durable field carries the discarded compiler tail.

Thus the actual compiler/MSBuild error and numeric exit code are unavailable in these retained records. This is the existing CARD-0844 defect, not a newly established cause such as the historical CARD-0469 CopyFakeClaude failure. Duplicate searches `landing verifier`, `verification_failed`, and `verification diagnostics` were completed with `card.ps1 search ... -Board Antiphon -All`; CARD-0844 and CARD-0469 were read in full. **No duplicate card was filed or bound to CARD-1514.** CARD-0844 remains Backlog (id `8b63ecb0-3f50-45b0-8dfd-cf67f501d1b7`).

Excluded by evidence: Windows-red test assertions and test teardown never ran; build-slot timeout did not occur; no rebase conflict caused either terminal refusal. Still live: introduced compile/build-target failure, inherited build behavior exposed by these candidates, resource/process failure during compilation, or another host-dependent build failure. The clean comparison on the same target and host does not distinguish those. A failed warm generation followed by a failed fresh generation also does not establish a warm-cache-only cause.

The Linux receipts in the two source reports build the Antiphon.Tests project with a different output invocation; they do not prove this Windows root-solution artifacts-path build. Their 313/18/0 and 59/1/0 pass/skip/fail counts are prior task reports, not tests performed here. No introduced/inherited attribution is defensible without the missing build error or a controlled reproduction.

## Supported procedure and recommendation

Do not use a narrower `-Verify` as a remedy. It **adds** tests after the same build. No `-SkipVerify` or native-red exemption is provided by the Land parameter set (`scripts/delegate.ps1:300`). The minimal-testing standing rule (`docs/orchestration-loop.md:623`) limits Code/Review selections and keeps the rc roster as the gate; it does not bypass a failed publication build. The documentation-only skip cannot apply to either candidate's C# fixture changes. Task placement on server2 does not relocate the local verifier.

What must happen first: after the active Windows qualification releases its lane, separately commission a desktop Debug owner to reproduce each exact rebased candidate above in its own isolated checkout, through `scripts/build-slot.ps1`, with the same root `dotnet build --artifacts-path <new-owned-output> --configuration Debug` command. Preserve stdout, stderr, numeric exit, candidate identity and any build diagnostics. Do not reuse/reset the live land worktree or execute its old binary. Compare the failing build at its pinned target base only if needed to attribute the observed error. An actual repair belongs in a separate Code/Docs task; none was designed or implemented here.

Only after that evidence establishes a successful build and all applicable approval/review requirements are satisfied may the caller retry unchanged sources with these standard commands. If source repair changes either tip, use the newly approved full SHA instead. These commands were **not executed**:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land 2a61a460 -ExpectedSourceSha b9e636a530d882a412a1ec93460c052338a65723
pwsh -NoProfile -File scripts/delegate.ps1 -Land 91ea9029 -ExpectedSourceSha 8c0bb33a554070ec2f1498206f91fd876ff3350e
```

No safe bypass command can be recommended from this evidence. Retry still rebases against the then-current target and verifies that candidate; prior Linux green or the comparison land does not authorize a failed Windows build. Native failing-class reruns and rc qualification remain separately owed.

## Not done, noted

Existing CARD-0844 requests retention and surfacing of bounded verifier failure output; no implementation or fix design was produced.

--- next stage ---
next: investigate
handoff: After db2c2088 releases the Windows lane, capture exact root-build output for rebased candidates d85ebbc9741f1f1cdba9a863d960e19f6c80726c and efd3a13f56ed1182ee01a62adeb482fb4ea42cdb in separate isolated checkouts; both had null filters and failed before tests. Existing CARD-0844 loses the error tail.
artifact: docs/investigations/2026-10-25-card-0844-land-verifier-discards-build-error-tail-verification-failed-is-undiagnosable.md