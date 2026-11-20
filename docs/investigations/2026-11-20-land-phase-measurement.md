# Land phase measurement (FT-0, 2026-11-20)

Slice FT-0 of [the fast-track lane plan](../superpowers/plans/2026-11-20-fast-track-lane-plan.md) (commit `52b66df5ed0193104514d8a27fb5604ded06c96e`, sections G1–G3 and 1). Read-only measurement on the desktop at base master `e54065fe59f798fb5dc395a222679545ebe1c9e0`. Task `dbcf227a`.

## Outcome

- **G1 inference: partly refuted, and the mechanism is confirmed.** Of 65 landed requests between 2026-11-19 00:00Z and 2026-11-20 09:15Z, **24 verification decisions were docs-only diffs**. Six of them **skipped** the build (`reason non-code`). Eighteen built (`reason unrecognized-build-graph`). The rule held in all 24 cases with no exception: the land skipped **if and only if** the candidate tree's build-graph fingerprint equalled the manifest embedded in the server that was running. "Every docs-only land builds" is true only while the running server's manifest differs from master's graph. That was true before the 2026-11-19 14:28Z restart, and again from 2026-11-20 01:50Z. It was false for about 11 hours in between.
- **The verify phase is a build only.** No land ran tests: no decision had `selected-tests`, so no `-Verify` filter was set (`AgentTaskLandService.cs:2019`). Every build was **warm**: 64 of 64 builds logged `land_artifacts warm` against one reused generation, `land-verify\v1\dcaeb6b9…\2967c738…`. All ran on the **desktop** (`C:\src\antiphon\.git\antiphon\land-verify`). A warm docs-only build still took **524 s median (454–757)**.
- **Land concurrency was always 1.** The 63 verifier build spans never overlapped, and the build slot wait was 0 s in 62 of 63 builds (one waited 77 s). Lands are serialized by the per-repository land execution lease (`AgentTaskLandService.cs:534-539`). The host build slot was not the bottleneck.
- **Phase split (median s, lease→done):**
  - Docs-only land that skipped: wall 186, of which cleanup 110 and push+confirm 26.
  - Docs-only land that built: wall 657, of which verify 524, cleanup 86 and push+confirm 23.
  - Code land: wall 823, of which verify 644.
  - Queue wait before the lease was 94 s median and up to 2691 s.

## Sources and method

- Server Serilog files on this desktop: `C:\src\Antiphon\server\logs\antiphon-20261119.log` and `antiphon-20261120.log` (`docs/logs.md:11`). Note that `C:\src\Antiphon\logs` holds AppHost and launcher output, not the server log. Only targeted lines were extracted. No log content is committed.
- Line templates used:
  - `Landing verification decision task … reason {Reason} target … candidate …` (`server/Application/Services/AgentTaskLandingProtocol.cs:338-340`).
  - `Land git profile task=… request=… outcome=… wallSeconds=… reset= rebase= verify= push= canonical= cleanup=` (`AgentTaskLandService.cs:568-576`).
    - The phases come from the operation's own timestamps (`PhaseSeconds`, `AgentTaskLandService.cs:1604-1614`).
    - `verify` is `VerificationStartedAt→VerifiedAt`; `push` is `PushStartedAt→RemoteConfirmedAt`.
    - `wall` starts **after** both leases are acquired (`:550`), so it excludes queue time.
  - `Landing verifier … land_artifacts warm|cold …`, `… BUILD SLOT granted … waited=Ns`, and `… BUILD SLOT released … held=Ns`.
  - `Request from ::1: POST /api/agent-tasks/<task>/land/v2`. The **queue** is the gap between the last POST before the lease and the lease time, which is the profile timestamp minus `wallSeconds`. A re-POSTed request is therefore measured from its last POST.
- Running server SHA per window:
  - Main-checkout reflog: `997f9b8d9` from 2026-11-18 18:18Z, then `26a99f1a7` checked out at 2026-11-19 14:24:10Z.
  - Server log `Now listening` at `antiphon-20261119.log:136071` (14:28:20Z). This is the only start in the window.
  - `GET /api/version` now returns `26a99f1a747d…`.
- Embedded manifests (`git show <sha>:server/Application/Services/LandVerificationBuildGraph.manifest.txt`):
  - `997f9b8d9` → `98B4ECF0…/80`.
  - `26a99f1a7` → `C5B7C0CE…/80`.
  - `e54065fe5` → `EA552D85…/80`.
- Candidate fingerprints were recomputed with the production selector and hash: `LandVerificationBuildGraph.Includes` / `FingerprintOf` over `git ls-tree -r -z <candidate>`. They were computed with a scratch Python port, not committed. The port is validated because it reproduces all three committed manifests exactly: `26a99f1a7`→C5B7C0CE, `997f9b8d9`→98B4ECF0 and `e54065fe5`→EA552D85.
- Change class is `git diff --name-only <target> <candidate>` at decision time, which is the same range the policy reads (`LandVerificationPolicy.cs:21-24`):
  - **docs** means every path is `docs/**.md` or `README.md`, the policy's own definition.
  - **test** means every path is under `docs/`, `tests/`, `README.md`, `AGENTS.md` or `CLAUDE.md`, with at least one non-docs path.
  - **code** means any other path, including `scripts/**`.
  - The reason column independently confirms the docs class, because `non-code` and `unrecognized-build-graph` are reachable only after `OnlyDocumentation` passes (`LandVerificationPolicy.cs:24-27`).

## Decision reasons (Q1)

| Decision reason | Count | Class | Candidate graph = running manifest? |
|---|---:|---|---|
| `diff-requires-build` | 45 | 38 code + 7 test | 17 match, 28 not; irrelevant, the diff already requires a build |
| `unrecognized-build-graph` | 18 | docs (all) | **no** in 18/18 |
| `non-code` (skipped) | 6 | docs (all) | **yes** in 6/6 |

There are 69 decision lines for 65 landed requests. Four requests built twice: `944f6e5f`, `b29f872a`, `bdfe801a` and `48037c93`. Their target moved while the verification window was released, so they re-decided. One further request, `d5d33a23` (task `ad11c378`), completed with no decision and no push in 16 s (`antiphon-20261119.log:231700`).

### Timeline of the master build graph against the running manifest

| Window (UTC) | Running server / embedded manifest | Master candidate graph | Docs-only decisions |
|---|---|---|---|
| 11-19 00:00 → 14:28 | `997f9b8d9` / `98B4ECF0` | `C5B7C0CE` (from 11-19 01:37; `3B34C941` before) | 12 × `unrecognized-build-graph` (`antiphon-20261119.log:43500` … `:131498`) |
| 11-19 14:28 → 11-20 01:50 | `26a99f1a7` / `C5B7C0CE` | `C5B7C0CE` | **6 × `non-code` skip** (`antiphon-20261119.log:154512, 186092, 192790, 212127, 213391, 231189`) |
| 11-20 01:50 → 04:37 | `26a99f1a7` / `C5B7C0CE` | `BE21100D`, then `2DA6404C` | none docs-only |
| 11-20 04:37 → 09:15 | `26a99f1a7` / `C5B7C0CE` | `EA552D85` | 6 × `unrecognized-build-graph` (`antiphon-20261120.log:44580, 73234, 80515, 90095, 93902, 96360`) |

In the window, master's graph changed at three commits: candidate `d663f5eed` (11-20 01:50), `d36d95412` (03:28) and `ac5dc1690` (04:37). Build files changed in the range `c83d14d48..e0f381c48` include `4fd9b8149`, `869fe194e`, `b2d766590`, `0dec5150e` and `b55b31fbb`. The plan's statement that "today the rc manifest is C5B7C0CE and master is EA552D85, so every docs-only land builds" is **correct from 2026-11-20 04:37Z onward**. It is **wrong for 2026-11-19 14:28Z–11-20 01:50Z**, when the skip fired on all six docs-only lands. The skip is therefore not dead, but it is fragile: it lives only from a server restart until the next master build-graph edit. That edit came about 11 h after this restart.

The plan's section 1 counts **5** docs-only lands out of 49. This log shows **23 docs-only landed requests**, 24 decisions, out of 65. The plan classified each land by the previous land's `verifiedSha`..this `verifiedSha`, and it counted only Succeeded tasks up to 08:42Z. This report classifies the exact target..candidate pair that the policy saw. The difference is in how lands are counted, not a contradiction of the mechanism.

## Phase split (Q2), median (min–max) seconds

`queue` is POST→lease. Every other column is from the `Land git profile` line. `wall` is lease→done. Phases do not quite sum to `wall` because of git and DB work between them (`gitSeconds` 65–124 s per land). For the four double-verified requests, `verify` shows only the last attempt; slot `held` covers both.

### By class

| Class | n | queue | reset | rebase | verify (build) | slot wait | push+confirm | cleanup | wall (lease→done) |
|---|---:|---|---|---|---|---|---|---|---|
| docs/skip | 6 | 100 (0-386) | 5 (4-6) | 1 (1-2) | 2 (2-6) | - | 26 (22-78) | 110 (103-209) | 186 (162-263) |
| docs/build | 17 | 129 (0-2691) | 4 (3-6) | 1 (1-2) | 524 (454-757) | 0 (0-0) | 23 (19-94) | 86 (75-107) | 657 (580-1083) |
| test | 7 | 47 (0-939) | 4 (3-6) | 1 (1-2) | 607 (509-927) | 0 (0-0) | 26 (22-30) | 102 (85-114) | 759 (653-1106) |
| code | 35 | 94 (0-1862) | 4 (3-10) | 2 (1-23) | 644 (475-788) | 0 (0-77) | 24 (20-86) | 98 (74-143) | 823 (611-1862) |
| all | 65 | 94 (0-2691) | 4 (3-10) | 1 (1-23) | 581 (2-927) | 0 (0-77) | 24 (19-94) | 95 (74-209) | 755 (162-1862) |

### Per land (all 65 landed requests; class (n files); graph = candidate fingerprint / running embedded manifest, first 8 hex; log line = the `Land git profile` line in `antiphon-2026<file>`)

| # | Start (UTC) | Task | Request | Class (files) | Decision reason | Candidate graph / server manifest | queue | reset | rebase | verify | slot wait | push+confirm | cleanup | wall | log line |
|---|---|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|
| 1 | 11-19 00:27:49 | 41da5c81 | 5e8bec75 | code (17) | diff-requires-build | 3B34C941 / 98B4ECF0 | 187 | 4.64 | 0.65 | 554.69 | 0 | 21.86 | 89.3 | 695 | 1119.log:4691 |
| 2 | 11-19 01:25:28 | afec64d4 | 37b476e8 | code (12) | diff-requires-build | 3B34C941 / 98B4ECF0 | 160 | 3.28 | 0.51 | 513.78 | 0 | 22.54 | 74.44 | 636 | 1119.log:12965 |
| 3 | 11-19 01:37:38 | 846a331c | 5b136e86 | code (24) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 209 | 3.34 | 2.1 | 493.08 | 0 | 20.82 | 92.68 | 633 | 1119.log:14801 |
| 4 | 11-19 02:53:33 | ee721a69 | d0d68d10 | code (3) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 95 | 3.35 | 0.52 | 520.14 | 0 | 19.7 | 74.48 | 635 | 1119.log:27845 |
| 5 | 11-19 03:43:08 | 6e03880d | 2c5c4cb0 | code (6) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 91 | 9.6 | 3.63 | 627.29 | 0 | 23.7 | 90.19 | 780 | 1119.log:37036 |
| 6 | 11-19 04:04:43 | a18cc863 | 382d8703 | code (7) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 94 | 3 | 1.56 | 545.29 | 0 | 31.55 | 91.99 | 696 | 1119.log:40235 |
| 7 | 11-19 04:37:18 | 3a8b93bd | ad436557 | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 93 | 4.72 | 1.29 | 706.06 | 0 | 94.38 | 94.04 | 923 | 1119.log:45481 |
| 8 | 11-19 05:42:42 | 26e01f09 | be61afff | code (9) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 93 | 3.63 | 1.48 | 733.94 | 0 | 23.12 | 94.64 | 886 | 1119.log:57006 |
| 9 | 11-19 06:45:57 | 87851070 | 3b93aa67 | docs (7) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 92 | 3.68 | 1.21 | 633.27 | 0 | 23.09 | 85.99 | 770 | 1119.log:67689 |
| 10 | 11-19 09:05:21 | 9f93d5fa | 39a3115b | code (10) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 91 | 3.67 | 0.86 | 580.68 | 0 | 24.37 | 89.49 | 722 | 1119.log:89723 |
| 11 | 11-19 09:43:56 | e9951cc3 | e8de3fda | code (29) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 92 | 3.66 | 2.45 | 603.19 | 0 | 23.29 | 97.81 | 755 | 1119.log:93713 |
| 12 | 11-19 10:03:33 | 17eb0a70 | ca49055a | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 93 | 3 | 0.99 | 454.11 | 0 | 21.99 | 89.87 | 591 | 1119.log:96116 |
| 13 | 11-19 10:16:43 | 6efbbc13 | 944f6e5f | docs (1) | unrecognized-build-graph+unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 129 | 5.95 | 0.83 | 465.79 | 0 | 21.93 | 76.52 | 1083 | 1119.log:99792 |
| 14 | 11-19 10:42:58 | 5184298c | ad0d540d | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 94 | 2.86 | 0.81 | 457.41 | 0 | 20.36 | 75.34 | 580 | 1119.log:102758 |
| 15 | 11-19 11:24:43 | 220f064b | dceae4a5 | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 157 | 4.32 | 0.64 | 523.82 | 0 | 20.81 | 87.43 | 657 | 1119.log:109917 |
| 16 | 11-19 11:35:41 | 20293d8d | ae53ae8a | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 813 | 3 | 0.97 | 523.94 | 0 | 20.19 | 86.25 | 650 | 1119.log:112179 |
| 17 | 11-19 11:48:03 | b68892ff | c43ae073 | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 1062 | 3.45 | 1.08 | 524.48 | 0 | 23.09 | 83.74 | 657 | 1119.log:114820 |
| 18 | 11-19 12:19:58 | 2086875f | 567ce505 | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 129 | 3.44 | 0.84 | 487.86 | 0 | 19.46 | 85.84 | 619 | 1119.log:120735 |
| 19 | 11-19 12:32:13 | 016f2cc2 | 9d8a6a9c | code (13) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 240 | 3.27 | 1.56 | 474.7 | 0 | 21.89 | 84.9 | 611 | 1119.log:122852 |
| 20 | 11-19 12:44:58 | e5626886 | bb008623 | code (3) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 94 | 3.02 | 1.12 | 527.48 | 0 | 24.44 | 84.55 | 659 | 1119.log:124917 |
| 21 | 11-19 12:57:28 | 5d81b5f8 | aed6eef7 | docs (1) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 201 | 3.31 | 1.01 | 511.7 | 0 | 21.26 | 84.21 | 643 | 1119.log:126370 |
| 22 | 11-19 13:43:23 | 27b27091 | c2b21e57 | docs (2) | unrecognized-build-graph | C5B7C0CE / 98B4ECF0 | 93 | 3.08 | 0.77 | 486.22 | 0 | 22.27 | 85.38 | 619 | 1119.log:132700 |
| 23 | 11-19 13:55:13 | 77ec2b4f | 0550877f | code (5) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 145 | 3.13 | 1.27 | 490.49 | 0 | 20.04 | 82.09 | 616 | 1119.log:134042 |
| 24 | 11-19 14:12:03 | da2ad302 | 15c20305 | test (3) | diff-requires-build | C5B7C0CE / 98B4ECF0 | 94 | 3.18 | 1.17 | 541.75 | 0 | 22.01 | 87.3 | 673 | 1119.log:135897 |
| 25 | 11-19 14:33:57 | 6852e148 | 38782f92 | code (6) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 3.71 | 1.98 | 788.48 | 77 | 22.96 | 132.08 | 971 | 1119.log:139962 |
| 26 | 11-19 14:50:08 | 5444bf45 | c664af32 | code (3) | diff-requires-build | C5B7C0CE / C5B7C0CE | 87 | 4.22 | 1.65 | 756.61 | 0 | 30.54 | 101.12 | 917 | 1119.log:143025 |
| 27 | 11-19 15:05:25 | 7222353c | 1ffd3b67 | code (3) | diff-requires-build | C5B7C0CE / C5B7C0CE | 929 | 3.32 | 1.53 | 686.06 | 0 | 28 | 119.93 | 869 | 1119.log:145494 |
| 28 | 11-19 15:19:54 | 3f0bbab5 | b29f872a | code (4) | diff-requires-build+diff-requires-build | C5B7C0CE / C5B7C0CE | 978 | 9.53 | 2.63 | 667.6 | 0 | 32.54 | 114.33 | 1862 | 1119.log:154310 |
| 29 | 11-19 15:51:36 | a5e3ce5c | b8a39885 | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 181 | 4.8 | 1.11 | 2.01 | - | 26.81 | 109.85 | 177 | 1119.log:155031 |
| 30 | 11-19 16:26:01 | f1b04f65 | 181fdb75 | test (5) | diff-requires-build | C5B7C0CE / C5B7C0CE | 47 | 4 | 1.2 | 606.62 | 0 | 24.82 | 105.61 | 759 | 1119.log:162727 |
| 31 | 11-19 16:54:22 | 34eae6f8 | ce88e1cc | test (3) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 3.62 | 1.42 | 555.45 | 0 | 27.26 | 101.07 | 713 | 1119.log:168135 |
| 32 | 11-19 17:08:08 | a994b73f | 61f50c6d | code (6) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 5.7 | 1.35 | 651.12 | 0 | 34.46 | 105.22 | 823 | 1119.log:170824 |
| 33 | 11-19 18:18:46 | 425c2be6 | 17251af0 | code (4) | diff-requires-build | C5B7C0CE / C5B7C0CE | 12 | 4.49 | 2.15 | 603.12 | 0 | 46.74 | 91.34 | 770 | 1119.log:181960 |
| 34 | 11-19 18:31:37 | c2d4f69e | dafa266f | code (12) | diff-requires-build | C5B7C0CE / C5B7C0CE | 190 | 3.44 | 1.62 | 743.79 | 0 | 58.61 | 142.52 | 977 | 1119.log:184396 |
| 35 | 11-19 18:58:41 | b0abf10c | ef567dcf | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 0 | 3.88 | 1.2 | 1.58 | - | 21.91 | 209.48 | 263 | 1119.log:186693 |
| 36 | 11-19 19:34:56 | 5de65634 | 4f5aa31f | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 235 | 4.64 | 0.85 | 1.77 | - | 25.11 | 103.13 | 162 | 1119.log:193326 |
| 37 | 11-19 19:41:44 | fa472ac7 | f5429aad | code (10) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 3.88 | 1.28 | 710.59 | 0 | 24.47 | 102.94 | 865 | 1119.log:197026 |
| 38 | 11-19 21:13:41 | 1800c3c0 | a9cf8741 | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 18 | 5.61 | 1.65 | 2.29 | - | 28.25 | 135.55 | 195 | 1119.log:212671 |
| 39 | 11-19 21:19:52 | f43b0b69 | 9d9cb600 | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 386 | 5.36 | 1.48 | 5.53 | - | 78.49 | 110.98 | 245 | 1119.log:214072 |
| 40 | 11-19 21:33:29 | 12129c89 | 6d740a6f | test (4) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 5.58 | 2.46 | 927.21 | 0 | 27.58 | 113.6 | 1106 | 1119.log:219406 |
| 41 | 11-19 21:51:55 | 265eefc9 | 78d51ffc | code (9) | diff-requires-build | C5B7C0CE / C5B7C0CE | 950 | 4.6 | 3.22 | 702.69 | 0 | 24.37 | 106.42 | 869 | 1119.log:222014 |
| 42 | 11-19 22:43:03 | 1b3802c5 | 8283d559 | code (3) | diff-requires-build | C5B7C0CE / C5B7C0CE | 1 | 6.56 | 3.54 | 709.83 | 0 | 27.56 | 103.78 | 879 | 1119.log:230214 |
| 43 | 11-19 23:02:19 | 6d7de792 | 191b0a6b | docs (1) | non-code | C5B7C0CE / C5B7C0CE | 14 | 4.99 | 1.17 | 1.81 | - | 22.72 | 105.89 | 163 | 1119.log:231657 |
| 44 | 11-20 00:03:28 | ec3f6919 | 87bc3833 | code (5) | diff-requires-build | C5B7C0CE / C5B7C0CE | 0 | 3.98 | 0.64 | 743.37 | 0 | 40.23 | 100.68 | 914 | 1120.log:3728 |
| 45 | 11-20 00:51:09 | 0cb46743 | 10a24b1b | code (3) | diff-requires-build | C5B7C0CE / C5B7C0CE | 24 | 4.63 | 1.18 | 603.02 | 0 | 22.47 | 92.89 | 748 | 1120.log:13176 |
| 46 | 11-20 01:36:23 | 2af26f95 | 8531d3b9 | code (9) | diff-requires-build | C5B7C0CE / C5B7C0CE | 2 | 4.15 | 2.8 | 644.34 | 0 | 24.33 | 116.9 | 819 | 1120.log:21812 |
| 47 | 11-20 01:50:03 | 377eda06 | 368f9082 | code (8) | diff-requires-build | BE21100D / C5B7C0CE | 243 | 3.85 | 22.76 | 719.79 | 0 | 85.99 | 142.82 | 1000 | 1120.log:24506 |
| 48 | 11-20 02:06:44 | 5a0fdc5f | ee1d4ec1 | code (5) | diff-requires-build | BE21100D / C5B7C0CE | 842 | 4.28 | 2.31 | 768.35 | 0 | 34.35 | 118.75 | 955 | 1120.log:27240 |
| 49 | 11-20 02:22:38 | 464ba2aa | ddab70c2 | code (39) | diff-requires-build | BE21100D / C5B7C0CE | 1617 | 3.29 | 2.8 | 787.26 | 0 | 24.78 | 131.65 | 977 | 1120.log:29114 |
| 50 | 11-20 03:28:14 | b81aa118 | 3783b180 | code (33) | diff-requires-build | 2DA6404C / C5B7C0CE | 0 | 5.59 | 3.65 | 691.63 | 0 | 27.61 | 104.63 | 861 | 1120.log:36190 |
| 51 | 11-20 03:55:08 | 4940cf9f | 761688b1 | code (4) | diff-requires-build | 2DA6404C / C5B7C0CE | 0 | 4.64 | 1.51 | 768.61 | 0 | 51.87 | 107.92 | 961 | 1120.log:39982 |
| 52 | 11-20 04:36:40 | 2aa3ce3b | 966987e2 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 0 | 4.71 | 1.54 | 719.48 | 0 | 31.42 | 107.33 | 894 | 1120.log:47075 |
| 53 | 11-20 04:51:34 | d591d78b | f14dad75 | test (7) | diff-requires-build | EA552D85 / C5B7C0CE | 542 | 5.38 | 2.4 | 684.01 | 0 | 25.76 | 103.31 | 847 | 1120.log:49576 |
| 54 | 11-20 05:26:25 | 808a934f | fc351ce9 | test (5) | diff-requires-build | EA552D85 / C5B7C0CE | 16 | 4.91 | 1.44 | 661.53 | 0 | 26.4 | 102.25 | 824 | 1120.log:56184 |
| 55 | 11-20 05:42:21 | 094c50ce | bdfe801a | code (10) | diff-requires-build+diff-requires-build | EA552D85 / C5B7C0CE | 0 | 7.63 | 4.16 | 681.63 | 0 | 45.34 | 111.3 | 1506 | 1120.log:60986 |
| 56 | 11-20 06:42:18 | 8f86afa1 | 8d3a73ed | code (13) | diff-requires-build | EA552D85 / C5B7C0CE | 0 | 3.5 | 0.59 | 614.27 | 0 | 22.37 | 91.74 | 757 | 1120.log:69032 |
| 57 | 11-20 07:23:05 | 188ceb65 | 03fd42d3 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 64 | 6.22 | 1.15 | 757.15 | 0 | 22.77 | 93.68 | 908 | 1120.log:75530 |
| 58 | 11-20 07:38:45 | cf20bef5 | 65b93190 | code (5) | diff-requires-build | EA552D85 / C5B7C0CE | 413 | 6.51 | 1.88 | 506.79 | 0 | 25.58 | 86.03 | 651 | 1120.log:78016 |
| 59 | 11-20 07:49:36 | b11c8906 | 3583cb6d | test (2) | diff-requires-build | EA552D85 / C5B7C0CE | 939 | 3.26 | 1.21 | 508.7 | 0 | 29.84 | 85.42 | 653 | 1120.log:80393 |
| 60 | 11-20 08:00:45 | ab9057c0 | eb051400 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 1493 | 3.52 | 0.87 | 497.89 | 0 | 24.29 | 86.18 | 636 | 1120.log:82881 |
| 61 | 11-20 08:11:21 | 5782c600 | 7765597b | code (9) | diff-requires-build | EA552D85 / C5B7C0CE | 1862 | 3.21 | 1.64 | 545.13 | 0 | 22.37 | 93.86 | 686 | 1120.log:84895 |
| 62 | 11-20 08:22:48 | 00e2b8c7 | 48037c93 | code (5) | diff-requires-build+diff-requires-build | EA552D85 / C5B7C0CE | 1684 | 6.91 | 1.67 | 541.9 | 0 | 24.87 | 90.21 | 1260 | 1120.log:90041 |
| 63 | 11-20 08:43:47 | cd9d14b8 | 26639306 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 2691 | 3.59 | 1.24 | 623.71 | 0 | 25.98 | 104.76 | 784 | 1120.log:92623 |
| 64 | 11-20 09:03:10 | 73d0252f | bbc809c9 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 17 | 3.74 | 1.28 | 527.09 | 0 | 26.55 | 95.35 | 679 | 1120.log:96293 |
| 65 | 11-20 09:14:29 | b7e96a42 | 02419231 | docs (1) | unrecognized-build-graph | EA552D85 / C5B7C0CE | 629 | 3.84 | 1.3 | 540.79 | 0 | 27.11 | 87.7 | 685 | 1120.log:99033 |

Reading the table:
- **Skipping saves about 470 s of lane time per docs-only land.** The median wall was 657 s for a docs-only land that built and 186 s for one that skipped. Over the 17 docs-only builds in the window, that is about 2.2 h of serial land-lane time.
- **A skipped land still costs about 3 min.** Cleanup is the largest remaining phase: 110 s median, 103–209 s. Push plus remote confirm is about 25 s. Reset plus rebase is about 6 s.
- **Cleanup costs 75–209 s on every land** (95 s median), about 1.5 h over 65 lands.
- **Queue wait is bursty.** The median is 94 s, but 2691 s (task `cd9d14b8`), 1862 s (`5782c600`), 1684 s (`00e2b8c7`), 1617 s (`464ba2aa`) and 1493 s (`ab9057c0`) occurred when several requests bunched together.

## Cold or warm, and which host (Q3)

- 64 of 64 builds logged `land_artifacts warm`, all on one generation directory: `c:\src\antiphon\.git\antiphon\land-verify\v1\dcaeb6b9…\2967c73855ec41648d7d62f82481e984`. There were no `cold` lines. The host is the desktop, which ran the AppHost server: `26a99f1a7`, then `997f9b8d9`.
- Build command: `dotnet build --artifacts-path <generation> --configuration Debug -maxcpucount:4` over the whole solution. `maxcpucount=4` comes from the slot grant (`AgentTaskLandService.cs:2012-2015`).
- **Warm does not mean cheap.** A docs-only diff built for 454–757 s against artifacts that were already warm. One static cause is visible but **not measured**: `Directory.Build.props:17-46` stamps `git rev-parse HEAD` into `SourceRevisionId` (CARD-0179 R3), and `IncludeSourceRevisionInInformationalVersion` defaults to true. Every new candidate SHA therefore changes the generated AssemblyInfo of every project, and with it every assembly and every dependent. If that is right, the warm generation saves restore and NuGet work but not compilation. **UNCONFIRMED.** A binlog or `-v:n` target summary of one docs-only land build would settle it.

## Concurrency (Q4)

- Land verifier builds ran **one at a time**: the 63 build spans (grant→release) had a maximum overlap of 1. The design enforces this through the per-repository land execution lease and the repository lease (`AgentTaskLandService.cs:534-548`). A land that finds the lease busy is held with `land_execution_busy`, or yields to dispatch: for example `"stops yielding after 93s with 1 lease waiter(s)"` at `antiphon-20261119.log:96569`.
- The build slot did not contend with other builds: `waited=0s` in 62 of 63 grants, and one grant waited 77 s. The server log has no non-land `BUILD SLOT` lines. The 127 unprefixed lines are duplicates of the 63 verifier grant and release pairs. Builds that other tasks ran on the desktop are recorded in the session-runner log and were not read here.

## Remaining uncertainties

- The 49-versus-65 land count. The plan's population (Succeeded tasks, verifiedSha ranges) was not re-derived from the API here. This report's population is every `Land git profile` line with `outcome=Complete/Landed` in the two log files.
- Why a warm docs-only build takes about 9 minutes. The SourceRevisionId hypothesis above is unmeasured.
- Queue time is measured from the last POST. A request that was held, re-POSTed or aged may have waited longer from its first POST.
- Cleanup at about 95 s per land is measured, but its breakdown (worktree release, canonical advance, branch deletion) is not in the profile line.

## Not done, noted

- Fix ideas only, not designed here:
  - FT-2 (trust a current target manifest) would have turned all 18 `unrecognized-build-graph` decisions in this window into skips, about 2.2 h of lane time.
  - A constant `SourceRevisionId` for land verify builds may cut every land's build. This needs measuring first.
  - The roughly 100 s cleanup phase is the next-largest fixed cost.
