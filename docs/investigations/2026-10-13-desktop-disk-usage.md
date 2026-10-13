# Desktop disk usage â€” 2026-10-13

Task `4e06595f`; desktop metadata inventory. **No deletion, cleanup, moves, repository builds, or tests were executed.** Only this Markdown deliverable is committed. The already-granted analysis/report authority was used without another approval request.

At 2026-10-13T09:32:53.151683+00:00, C: capacity **1999.733 GB**, used **1632.946 GB** (81.66%), free **366.787 GB**. Windows binary equivalents: 1862.40 GiB capacity, 341.60 GiB free. Initial brief figures were binary-sized despite the GB label.

**Units and limits:** GB = 1,000,000,000 bytes; three-decimal values are measured logical lengths rounded to 1 MB, not promises of physical space returned. Exact bytes are retained in local metadata evidence. Nearest-1-GB columns satisfy the broad ranking requirement. Parent/child rows overlap and must not be added. Hard links are not deduplicated; NTFS compression, sparse files, cloud placeholders, inaccessible files, and filesystem overhead prevent logical totals from exactly matching volume usage. Virtual disks additionally show allocated bytes where readable. No payload, log, transcript, credential, or personal document contents were opened. Repository instructions and script owners were read for safety.

Walk: a 900-second pass plus a concurrent 85-second sample outside Antiphon within the same wall-clock allowance; combined metadata: 900.0 seconds, 9,261,798 files, 992,775 directories; timed out=True; 325 metadata/access errors; 3,026 reparse directories excluded to avoid junction cycles or following external roots. `partial` means a lower bound; `unknown` never means zero. Cloud reparse exclusions are detailed below. Live files and tasks can change during this non-atomic inventory.

## Top five reclamation candidates

**Coverage outcome:** Antiphon and C:\src were traversed completely for accessible non-reparse content. The complementary sample reached Docker, WSL Packages and part of Local Temp. Roaming, the remaining Local caches, profile data (including NuGet/Claude/Codex/Dropbox/OneDrive), Windows, XboxGames and later roots were not recursively measured. The 20-file/folder rankings are therefore **largest observed**, not a certified complete-machine ranking. The combined accessible logical total is 789.465 GB against 1632.946 GB physical used; the difference is unassigned, not a reclaim estimate. The brief explicitly allowed sampling when the walk was slow. Resolve these gaps with a separate targeted inventory budget before claiming a complete accounting.

| Candidate | Estimated addressable GB | Class | Conditions and human command text (not run) |
| --- | --- | --- | --- |
| Worktree directories mapped to terminal tasks | 178.619 | stale Antiphon artifact | Includes retained source/evidence and cache subcategories below. No unconditional safe removal command is established; review through `pwsh -NoProfile -File C:\src\Antiphon\scripts\worktree-residue.ps1 -Action Preview -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -Token $env:ANTIPHON_TASK_TOKEN`, then the owner-validated retirement route. Preview reclaims 0 GB. |
| Sampled `antiphon-land-verify-*` directories in Local Temp | 168.070 | unknown | Potential old verification outputs; confirm executors ended and evidence retention before exact-path deletion. Largest concrete example: `Remove-Item -LiteralPath 'C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-901564b33eeb4d709f27e7fac744b170' -Recurse -Force` (2.391 GB for that path only). |
| Alternate bin-* outputs across worktrees | 16.347 | regenerable cache | Overlaps terminal worktrees. Review producers per tree; existing helper example: `pwsh -NoProfile -File C:\src\Antiphon\scripts\cleanup-build-junk.ps1 -RepoRoot C:\Antiphon\worktrees\card-task-2d76eab4`. That command addresses only that one tree, not all 16.347 GB. |
| Docker unused images | 14.430 | regenerable cache | `docker image prune -a`; removes images unused by containers, potentially including valuable rollback images. This is Docker's estimate, not guaranteed immediate host-VHDX shrink. |
| Worktree obj directories | 12.872 | regenerable cache | Overlaps terminal worktrees; stop no processes in this task. After producer review an exact example is `Remove-Item -LiteralPath 'C:\Antiphon\worktrees\card-task-722d7d90\tests\Antiphon.Tests\obj' -Recurse -Force` (0.094 GB for that one path). |

**Do not add these five rows:** cache rows overlap whole worktrees; the Temp row has unverified retention/custody and is not certified reclaimable. No reclamation total has been authorized or demonstrated. Largest observed complete installed game footprint is Steam 106.541 GB (ELDEN RING 54.962 GB), and Riot Games 40.078 GB; these are installed data to keep until the operator selects an uninstall.

### Sampled Local Temp verification directories

232 observed `antiphon-land-verify-*` directories, 168.070 GB, within the incomplete 168.112 GB Temp sample. `docs/testing-and-build.md:279-285` says the landing verifier retains its unique external artifacts directory. That documents a retention mechanism, not proof that any particular executor is inactive. No receipt or file contents were opened.

| Largest sampled directory | GB | Class |
| --- | --- | --- |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-901564b33eeb4d709f27e7fac744b170` | 2.391 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-26d3325bdb774b54882cd6a8a846fcd1` | 2.391 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-3aaf3eeef685420fa03cd6625932c96e` | 2.391 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-57a3d3b31523464c921a0210247b9f7d` | 2.391 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-17fd38b7805a4e94823cf2ad5ea94fab` | 2.390 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-5ac8b883f7be459aaf514b6dae4a336b` | 2.389 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-4be78e50691a4e9683788502e6dd4835` | 2.387 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-96997faaaf65430d8b92d6bccf1a4317` | 2.387 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-7963eb86fbfb45d78dcb3b850801e210` | 2.386 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-1d8d0e692a704c90aae8bd9b0c714f9e` | 2.386 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-104c262c57964087b42686372fb86313` | 2.385 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-97c715ca1f2b407589dd524f95c0510e` | 2.385 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-6fb7a5196ec14588881070edd7a30fb8` | 2.385 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-0b15000698b94ee094df70721c36be53` | 2.385 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-285af3a49ef34110a70537a51d6f328b` | 2.383 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-052b75e7f60e40ec8f4cf1fff7af6390` | 2.383 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-5e28f5c8e22b43988747cdc8362b6d5c` | 2.383 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-41cc320bf6a040c2bf4b951583cb30b4` | 2.382 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-0daccc683c974ba885bcfd6457e1e0cf` | 2.382 | unknown |
| `C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-48a84cadbace4a73ad80aeb2ae06bc7b` | 2.382 | unknown |


## 1. Top-level C:

| Item | GB nearest 1 | GB measured | Class | Human reclaim command (not run) |
| --- | --- | --- | --- | --- |
| `C:\Users` | 240 partial | 239.587 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Antiphon` | 191 | 191.330 | unknown | â€” retain / ownership decision required |
| `C:\Program Files (x86)` | 124 partial | 124.001 (partial) | data to keep | â€” retain / ownership decision required |
| `C:\Program Files` | 52 partial | 52.077 (partial) | data to keep | â€” retain / ownership decision required |
| `C:\src` | 41 | 40.627 | unknown | â€” retain / ownership decision required |
| `C:\Riot Games` | 40 | 40.078 | unknown | â€” retain / ownership decision required |
| `C:\ProgramData` | 18 partial | 17.917 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Python310` | 12 | 11.878 | unknown | â€” retain / ownership decision required |
| `C:\logs` | 10 | 9.644 | unknown | â€” retain / ownership decision required |
| `C:\Ruby31-x64` | 1 | 1.042 | unknown | â€” retain / ownership decision required |
| `C:\temp` | 1 partial | 0.955 (partial) | unknown | â€” retain / ownership decision required |
| `C:\plex-movies` | 1 | 0.712 | data to keep | â€” retain / ownership decision required |
| `C:\$Recycle.Bin` | 0 partial | 0.166 (partial) | unknown | â€” retain / ownership decision required |
| `C:\AppData` | 0 | 0.000 | unknown | â€” retain / ownership decision required |
| `C:\Recovery` | 0 partial | 0.000 (partial) | unknown | â€” retain / ownership decision required |
| `C:\$WinREAgent` | 0 | 0.000 | unknown | â€” retain / ownership decision required |
| `C:\Documents and Settings` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\inetpub` | 0 | 0.000 | unknown | â€” retain / ownership decision required |
| `C:\OneDriveTemp` | 0 | 0.000 | data to keep | â€” retain / ownership decision required |
| `C:\PerfLogs` | 0 | 0.000 | unknown | â€” retain / ownership decision required |
| `C:\System Volume Information` | unknown | unknown (access denied) | unknown | â€” retain / ownership decision required |
| `C:\tmp` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Tools` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\windmill` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Windows` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\XboxGames` | unknown | unknown | unknown | â€” retain / ownership decision required |

## 1. User profile

| Item | GB nearest 1 | GB measured | Class | Human reclaim command (not run) |
| --- | --- | --- | --- | --- |
| `C:\Users\lndco\AppData` | 240 partial | 239.584 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.android` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.aspire` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.aws` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.azure` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.bmad` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.bundle` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.cache` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.cagent` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.cargo` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.claude` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.claude-json-backups` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.cloudflared` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.codex` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.config` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.copilot` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.dbus-keyrings` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.dcp` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.docker` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.dotnet` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.dropbox_bi` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.grok` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.herdr` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.local` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.memsearch` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.ms-oauth` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.node` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.nuget` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.Origin` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.pnpm-store` | unknown | unknown | regenerable cache | `Remove-Item -LiteralPath 'C:\Users\lndco\.pnpm-store' -Recurse -Force` |
| `C:\Users\lndco\.QtWebEngineProcess` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.rustup` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.ssh` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.storybook` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.templateengine` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.vscode` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.vscode-shared` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\3D Objects` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\ansel` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Application Data` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\chrome-cdp` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Contacts` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Cookies` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Documents` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\dotTraceSnapshots` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Downloads` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\Dropbox` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\edge-cdp` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Favorites` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\leyton-cricket-ground` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Links` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Local Settings` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\memtrace` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Music` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\My Documents` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\NetHood` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\node_modules` | unknown | unknown | regenerable cache | `Remove-Item -LiteralPath 'C:\Users\lndco\node_modules' -Recurse -Force` |
| `C:\Users\lndco\OneDrive` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\Pictures` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\PrintHood` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Recent` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\RiderProjects` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Saved Games` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Searches` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\SendTo` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\SmashBrawl` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\source` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Start Menu` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Templates` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Tracing` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Ubiquiti UniFi` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\Videos` | unknown | unknown | data to keep | â€” retain / ownership decision required |

## 1. AppData Local

| Item | GB nearest 1 | GB measured | Class | Human reclaim command (not run) |
| --- | --- | --- | --- | --- |
| `C:\Users\lndco\AppData\Local\Temp` | 168 partial | 168.112 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Docker` | 63 | 63.305 | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Packages` | 8 | 8.167 | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\.IdentityService` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\AnthropicClaude` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Antiphon` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Application Data` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ASP.NET` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\audacity` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\authy` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\AWSToolkit` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\babl-0.1` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Backup` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Bytedance` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\cache` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\CapCut` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\CEF` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Claude` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Claude-3p` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\claude-cli-nodejs` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Codex` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Comms` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ConnectedDevicesPlatform` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\CrashDumps` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\CtxUnleashClient` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\CyberLink` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Cypress` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\D3DSCache` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\DBG` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\dcp-work` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\deno` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Discord` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\docker-secrets-engine` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Dropbox` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\DropboxUpdate` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\enchant` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Fallout4` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\fanal` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\fontconfig` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\GameAnalytics` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\gegl-0.4` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\GIMP` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\GitHub CLI` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\github-copilot` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\GitKrakenCLI` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\gk` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Google` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Greenshot` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\grok` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\gtk-2.0` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\gym-stat` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\herdr` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\History` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\IdentityNexusIntegration` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\IsolatedStorage` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\JetBrains` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\KillHouseGames` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Microsoft` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Microsoft SDKs` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Mozilla` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ms-playwright` | unknown | unknown | regenerable cache | `npx playwright uninstall --all` |
| `C:\Users\lndco\AppData\Local\MyLifeOrganized` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\net.codeperf.mdpkg` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\net.codeperf.mdpkgspike` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\node` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\NordVPN` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Notepad` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\npm-cache` | unknown | unknown | regenerable cache | `npm cache clean --force` |
| `C:\Users\lndco\AppData\Local\NuGet` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\NVIDIA` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\NVIDIA Corporation` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Obsidian` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\obsidian-updater` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Omnissa` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\OneDrive` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\OpenAI` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Origin` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Package Cache` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\PackageManagement` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\PCHealthCheck` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\PeerDistRepub` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\pip` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\PlaceholderTileLogoFolder` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\plastic4` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Plex Media Server` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\pnpm` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\pnpm-cache` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\pnpm-state` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Programs` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Publishers` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\qBittorrent` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\RefSrcSymbols` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\RescueTime.com` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Riot Games` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Seiko_Epson_Corporation` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Sentry` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ServiceHub` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\speech` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\SquirrelTemp` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Steam` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Symbols` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\SymbolSourceSymbols` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\synologyimageassistant-updater` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Tailscale` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\tauri` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\TeamViewer` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Temporary Internet Files` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ToastNotificationManagerCompat` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Ubisoft Game Launcher` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Unity` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\unityhub-updater` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\uv` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\VEDetector` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\VirtualStore` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\VMware` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Yarn` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Zoom` | unknown | unknown | unknown | â€” retain / ownership decision required |

## 1. AppData Roaming

| Item | GB nearest 1 | GB measured | Class | Human reclaim command (not run) |
| --- | --- | --- | --- | --- |
| `C:\Users\lndco\AppData\Roaming\.minecraft` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Adobe` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\astro` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\audacity` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Authy Desktop` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Bitwarden CLI` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Claude` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Claude Code` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Claude-3p` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Code` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Codex` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\CyberLink` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\discord` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Docker` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Docker Desktop` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\docker-secrets-engine` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\droidcam-obs-client` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Dropbox` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\DropboxElectron` | unknown | unknown | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\EasyAntiCheat` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\EldenRing` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\EPSON` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\GIMP` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\GitHub CLI` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Greenshot` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\HandBrake` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\herdr` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\himalaya` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\HMRC` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\inkscape` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\InstallShield` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\JetBrains` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\KeePass` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\LibreOffice` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\LocalSend` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Microsoft` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Mozilla` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Mp3tag` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\MPC-HC` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\mssdk` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\MyLifeOrganized` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\npm` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\NuGet` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\NVIDIA` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\nvidia-broadcast` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\obs-studio` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\obsidian` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Omnissa` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\OpenVPN Connect` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Origin` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Pentablet V3` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Python` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\qBittorrent` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Resilio Sync` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Scooter Software` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\SpecFlow` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Synapse3` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Synology Image Assistant` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\syntevo` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\tailscale` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\TeamViewer` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\trakt-scrobbler` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Typora` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Unity` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\UnityHub` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\uv` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Visual Studio Setup` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\vlc` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\VMware` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\Zoom` | unknown | unknown | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Roaming\ZoomVDI` | unknown | unknown | unknown | â€” retain / ownership decision required |

## 2. Docker Desktop and WSL

Docker context: `desktop-linux`. WSL inventory reports Ubuntu and docker-desktop running, both WSL2. Registered roots are Ubuntu LocalState and Docker wsl/main. C: is the only local filesystem drive observed. Every `.vhdx` found in the accessible walk is listed; inaccessible and skipped subtrees remain coverage gaps.

| Virtual disk path | Logical GB | Exact logical bytes | Allocated GB | Class | Human command |
| --- | --- | --- | --- | --- | --- |
| `C:\Program Files\Docker\Docker\resources\wsl\ext4.vhdx` | 0.109 | 109051904 | 0.109 | data to keep | â€” retain; do not delete a distribution or Docker data disk |
| `C:\Users\lndco\AppData\Local\Docker\wsl\disk\docker_data.vhdx` | 62.968 | 62968037376 | 62.968 | data to keep | â€” retain; do not delete a distribution or Docker data disk |
| `C:\Users\lndco\AppData\Local\Docker\wsl\main\ext4.vhdx` | 0.109 | 109051904 | 0.109 | data to keep | â€” retain; do not delete a distribution or Docker data disk |
| `C:\Users\lndco\AppData\Local\Packages\CanonicalGroupLimited.UbuntuonWindows_79rhkp1fndgsc\LocalState\ext4.vhdx` | 5.603 | 5602541568 | 5.603 | data to keep | â€” retain; do not delete a distribution or Docker data disk |

| Docker item | Count | Active | Docker size | Docker reclaimable | Class | Human reclaim command (not run) |
| --- | --- | --- | --- | --- | --- | --- |
| Images | 63 | 5 | 20.61GB | 14.43GB (70%) | regenerable cache | `docker image prune -a` |
| Containers | 5 | 5 | 1.726kB | 0B (0%) | data to keep | â€” keep running containers / inspect named volume custody |
| Local Volumes | 39 | 5 | 3.232GB | 1.563GB (48%) | unknown | â€” keep running containers / inspect named volume custody |
| Build Cache | 174 | 0 | 7.499GB | 0B | regenerable cache | `docker builder prune -a` |

The verbose inventory was captured with `docker system df -v`. Image layers/build cache overlap: Docker reports **0B independently reclaimable build cache**, despite 7.499GB cache usage. Do not add that 7.499GB to the 14.43GB image reclaim estimate. There are five running containers and no stopped containers to reclaim. Unused volume status is not permission to discard databases. Removing content inside Docker need not immediately shrink the host VHDX; no compaction was attempted or estimated.

## 3. Antiphon footprint

| Item | GB | What it is | Class | Human command |
| --- | --- | --- | --- | --- |
| `C:\src\Antiphon` | 7.282 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\src\Antiphon\.git` | 2.042 | Git object/admin store | data to keep | â€” retained until output/evidence custody is checked |
| `C:\Antiphon` | 191.330 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\Antiphon\worktrees` | 182.365 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\Antiphon\review-evidence` | 1.790 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\Antiphon\code-evidence` | 0.000 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\Antiphon\nightly` | 1.835 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\Antiphon\evidence` | 5.120 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\src\Antiphon\client\dist` | 0.006 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\src\Antiphon\logs` | 0.286 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |
| `C:\logs` | 9.644 | Antiphon checkout, retained evidence, build/runtime data; see category tables | unknown | â€” retained until output/evidence custody is checked |

### Build/ignored bulk in `C:\src\Antiphon`

| Category | Disjoint outer directories | GB | Class | Human command |
| --- | --- | --- | --- | --- |
| bin-* | 1 | 0.000 | regenerable cache | `pwsh -NoProfile -File 'C:\src\Antiphon\scripts\cleanup-build-junk.ps1' -RepoRoot 'C:\src\Antiphon'` |
| bin | 65 | 2.134 | unknown | â€” select a producer-owned exact path after review |
| obj | 66 | 0.268 | regenerable cache | â€” select a producer-owned exact path after review |
| node_modules | 2 | 0.578 | regenerable cache | â€” select a producer-owned exact path after review |
| TestResults | 8 | 0.001 | unknown | â€” select a producer-owned exact path after review |
| logs | 1018 | 0.783 | unknown | â€” select a producer-owned exact path after review |

### Build/ignored bulk in `C:\Antiphon\worktrees`

| Category | Disjoint outer directories | GB | Class | Human command |
| --- | --- | --- | --- | --- |
| bin-* | 671 | 16.347 | regenerable cache | â€” select a producer-owned exact path after review |
| bin | 119 | 0.432 | unknown | â€” select a producer-owned exact path after review |
| obj | 2751 | 12.872 | regenerable cache | â€” select a producer-owned exact path after review |
| node_modules | 10 | 1.425 | regenerable cache | â€” select a producer-owned exact path after review |
| TestResults | 15 | 0.002 | unknown | â€” select a producer-owned exact path after review |
| logs | 107 | 0.018 | unknown | â€” select a producer-owned exact path after review |

Categories remove ancestor/descendant duplicates within a category. Different categories and whole worktree totals still overlap. `.gitignore:1-9,25-26,46-55` establishes bin/obj/node_modules/dist/logs/bin-* exclusions; this is known ignored bulk, not an exhaustive untracked-file classification. No broad `git clean` command is proposed. `scripts/cleanup-build-junk.ps1:23-45` confines its existing helper to bin-* at depth 3 and skips directories modified within 60 minutes; `docs/testing-and-build.md:279-285` documents that behavior. A bin-* name by itself does not establish current producer custody; coordinate with live owners before running it.

### Checkpoint bootstrap outputs

36 bootstrap directories; summed logical size 0.556 GB (overlaps build/worktree totals).

| Largest bootstrap directories | GB | Class | Human command |
| --- | --- | --- | --- |
| `C:\Antiphon\worktrees\card-task-cc78d17e\tools\Antiphon.Checkpoints\bin-c1210-d1-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-722d7d90\tools\Antiphon.Checkpoints\bin-c722d-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-5914f175\tools\Antiphon.Checkpoints\bin-c5914-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\review-evidence\e3c11469\scratch\tools\Antiphon.Checkpoints\bin-c1042-review-tool-e3c11469` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-2d76eab4\tools\Antiphon.Checkpoints\bin-c820-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-752514ff\tools\Antiphon.Checkpoints\bin-c1188-review-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-00c23442\tools\Antiphon.Checkpoints\bin-review-00c23442-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-25cb35dd\tools\Antiphon.Checkpoints\bin-c820-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-2d76eab4\tools\Antiphon.Checkpoints\bin-c820-s4-base` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-034f38be\tools\Antiphon.Checkpoints\bin-c1047-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-0b309def\tools\Antiphon.Checkpoints\bin-c820-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-c1305a37\tools\Antiphon.Checkpoints\bin-c1047-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-fa10e904\tools\Antiphon.Checkpoints\bin-c1047-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-1d0f6684\tools\Antiphon.Checkpoints\bin-c1049-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-214410b4\tools\Antiphon.Checkpoints\bin-c975-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-2ba71f6e\tools\Antiphon.Checkpoints\bin-c1047-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-47b86ea8\tools\Antiphon.Checkpoints\bin-c1047-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-94914cdf-base\tools\Antiphon.Checkpoints\bin-c1030-triage-base` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-1363353b\tools\Antiphon.Checkpoints\bin-c1045-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |
| `C:\Antiphon\worktrees\card-task-33a85072\tools\Antiphon.Checkpoints\bin-c1039-tool` | 0.020 | regenerable cache | â€” coordinate active tool/executor ownership before exact-path removal |

### Worktree state and staleness evidence

Directory inventory: 1008 children. Git registry snapshot: 1 worktrees including the canonical checkout. GET lookups matched task IDs embedded in folder names: non-task folder=5, Failed=82, Succeeded=854, Canceled=60, Dispatched=3, Queued=2, Working=2. The lookup script accepts 8-hex IDs and retains only status/identity/landing metadata, not task prose.

| Task state | Measured worktree GB | Class |
| --- | --- | --- |
| Succeeded | 139.590 | stale Antiphon artifact |
| Failed | 24.511 | stale Antiphon artifact |
| Canceled | 14.517 | stale Antiphon artifact |
| unknown | 2.984 | unknown |
| Dispatched | 0.455 | data to keep |
| Working | 0.304 | data to keep |
| Queued | 0.003 | data to keep |

**Stale candidate rule:** branch contained by the saved local `origin/master`, or task Succeeded/Failed/Canceled. This is detection, not deletion authority. `origin/master` was `c2d0085a9ac7ae7989217a892df6b8c8f26df35e`; no fetch was run, so containment is against that local snapshot. A branch marked unmerged may have been rebased/squashed and landed; status alone does not prove publication. Dirty/ignored content, live sessions, saved receipts and pending use were not individually inspected. `docs/orchestration-loop.md:1669-1692` explicitly retains uncertain residue and ships `WorktreeResidue:Execute=false`; `docs/testing-and-build.md:331-334` leaves whole completed-card removal to CARD-1017.

#### Ten biggest

| Worktree | GB | GET task status | Branch | Merged into saved origin/master | Stale | Class |
| --- | --- | --- | --- | --- | --- | --- |
| `C:\Antiphon\worktrees\card-task-37267e24` | 5.840 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0726e42d` | 5.389 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e3922461` | 3.601 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c55b6c31` | 2.311 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a6fe70f2` | 1.578 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5e929ac1` | 1.576 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\c467-next-7e73872f` | 1.569 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-2d76eab4` | 1.524 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-94914cdf` | 1.511 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-94914cdf-base` | 1.487 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |

#### All worktree children (ranked)

| Worktree | GB | GET task status | Branch | Merged | Stale | Class |
| --- | --- | --- | --- | --- | --- | --- |
| `C:\Antiphon\worktrees\card-task-37267e24` | 5.840 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0726e42d` | 5.389 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e3922461` | 3.601 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c55b6c31` | 2.311 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a6fe70f2` | 1.578 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5e929ac1` | 1.576 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\c467-next-7e73872f` | 1.569 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-2d76eab4` | 1.524 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-94914cdf` | 1.511 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-94914cdf-base` | 1.487 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c9a3d250` | 1.071 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\rv37267e24-mb` | 1.066 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-47b86ea8` | 0.883 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dfc6f878` | 0.701 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-034f38be` | 0.548 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5bc00d31` | 0.514 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8db33c5e` | 0.498 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-272701b6` | 0.415 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-722d7d90` | 0.328 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cc78d17e` | 0.328 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5914f175` | 0.323 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e778e0b8` | 0.319 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d07857d1` | 0.319 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a2aa2d2` | 0.318 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1527665f` | 0.317 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-752514ff` | 0.316 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-00c23442` | 0.315 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-781e698a` | 0.311 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-44f2ecee` | 0.310 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-091ad0a4` | 0.298 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d7a4f178` | 0.296 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bd1f25ea` | 0.296 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-83407ae4` | 0.295 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ddd16c07` | 0.295 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce64becf` | 0.295 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-25cb35dd` | 0.291 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3e2722af` | 0.291 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-187ba700` | 0.291 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0d499136` | 0.291 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0b309def` | 0.288 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-214410b4` | 0.287 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86e61604` | 0.285 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1d0f6684` | 0.285 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fa10e904` | 0.285 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2ba71f6e` | 0.284 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c1305a37` | 0.283 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-33a85072` | 0.281 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1363353b` | 0.281 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e0239b6f` | 0.279 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c742d854` | 0.278 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-28a1714e` | 0.276 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-20b4b6ad` | 0.272 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d63c1fcc` | 0.271 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b33c60a0` | 0.268 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0192f22e` | 0.268 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd9653b8` | 0.267 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7ce891eb` | 0.267 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-80ff713f` | 0.267 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e9e898c2` | 0.267 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ab8a329e` | 0.267 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4095e3f8` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cc5e0224` | 0.266 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2618ecd0` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d32fc8af` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ad33afac` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3516100f` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e9e898c2-base` | 0.266 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6dcdf2c2` | 0.265 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4af9428c` | 0.265 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-22e766b4` | 0.264 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-527a8982` | 0.264 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b1bf4404` | 0.264 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a89289fd` | 0.263 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d2a56eff` | 0.263 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-de5c3888` | 0.262 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-616e0c1c` | 0.262 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3a6a7498` | 0.261 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4608a549` | 0.261 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8001f76` | 0.261 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-133888ed` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2299fe11` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a02625fa` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-022ccaf0` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-81dea5db` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a926b0dd` | 0.260 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0025fbfd` | 0.259 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0fd2a915` | 0.259 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8bd36a07` | 0.259 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4009b45a` | 0.259 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5128ef1f` | 0.259 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce92d660` | 0.259 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d7e561a7` | 0.258 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9efba31e` | 0.244 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c1a38371` | 0.244 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-92c73242` | 0.244 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5638a6b5` | 0.236 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-06e30249` | 0.226 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-50c43b3a` | 0.223 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2db75657` | 0.222 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ca7c02fa` | 0.221 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-CARD-0004` | 0.185 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-b9c5b5e6` | 0.176 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a0e06854` | 0.174 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-68855ec6` | 0.169 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cea60832` | 0.169 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-292d0813` | 0.164 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\land` | 0.162 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-66704481` | 0.162 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-413cf221` | 0.160 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-efc760e0` | 0.156 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6e5693b9` | 0.156 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7f1aaff2` | 0.153 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d5091e82` | 0.153 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-123472ac` | 0.153 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4e06595f` | 0.153 | Dispatched | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-a7f248c0` | 0.153 | Working | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-5b4007a2` | 0.153 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-48fbe811` | 0.153 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ebf0a2f0` | 0.153 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f5ac8bb8` | 0.153 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0ced9d05` | 0.152 | Dispatched | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-902bc6d6` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c162e333` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-604cc024` | 0.152 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e99f75c4` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0039718c` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8bcfa5da` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7b7d644a` | 0.152 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-84176c0c` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0734866e` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cadcf040` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-353dd304` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-578ae84e` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f6c26cd9` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3e736c72` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6c3dfed9` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-15a9381e` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b80e5c9e` | 0.152 | Working | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-e5579cd3` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8c713d0c` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9694c487` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-80f776ce` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b1b731e0` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c4cbce6e` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-842dd560` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c533d6c6` | 0.152 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-93ad5325` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2cb07437` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-71c428e1` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cbce1a3f` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dab30ee3` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-000c569b` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d3cc8360` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e4b3ff1c` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-81eb612a` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-415fcb98` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-54ad209e` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4ede83bf` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a835b016` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e02647de` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-16459e79` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0c7c3e0` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bc957ed9` | 0.151 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a3d75f7` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d25ba49f` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6bfc22bf` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-626eec47` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8da91605` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-250706b1` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e07d1562` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0ee48ce0` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2ec97f2d` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8295e472` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-96f3df54` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-49cee229` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-12b3b0bd` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-97835e71` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0d59c0d` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce4980da` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-04a4131f` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-79817d7a` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8173c35` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-04c02708` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-535e4326` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0bc8977` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0885df4` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0882c3c7` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2086c5d2` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5d2d0e80` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cec16163` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-adf0e6ba` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b5a3ee2f` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e6aea1d5` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-afb235c4` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2c70c12c` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-addee829` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2c9d87dd` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b0e6162a` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f13b23e4` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-494219e8` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-768488ed` | 0.151 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a7663fe9` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7188a896` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-75216093` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da4cdf51` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b113f1fa` | 0.151 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-270e01f3` | 0.151 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-04839ba2` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-88ffed25` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c3814b00` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bcba8cf7` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f1ece864` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9ed5ee58` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f88d5ef7` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f59ffbd2` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7bbab731` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cac7e685` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8992498` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fc488148` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-feafa656` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-40fe843e` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ef0bb4ed` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bf188fd1` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da085af8` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-308bbf35` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4756cf4b` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4e70ffd0` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cdef52d8` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4a35b4b6` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-57268864` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-23fc90c0` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-04655af5` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a12bbeb` | 0.150 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-58c3df41` | 0.150 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-443a4222` | 0.149 | Dispatched | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-b56c49d8` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-059e44d6` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-52c02a24` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c962f32c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cee24c68` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1baae4ee` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3f0e5491` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c7cf7371` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f9bcd692` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-108a9cea` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2cf84f6c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3ac09955` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6c9fe082` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e4073ef5` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-23285d2c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-53992a72` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-71060864` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3c07676a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fb9c2e7a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cadf4f62` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b48a7bff` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dfa1ed5b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b8d379f3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d1365602` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d904c7f6` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9b90ffac` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-07c7df01` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-29c608fe` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-29f384b1` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6b75a131` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9dea6ddd` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-899f86dc` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-45932800` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-18327db7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f503e575` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2000a0d1` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-771fa4ce` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-261a43a2` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-44a8eca2` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6f4b657d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-96f951b3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a6077026` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5091044b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a669127` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-149ee904` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b2f5300d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3a846ebf` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7971bc66` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c8c2ae56` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d5014684` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dd2619a1` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cf80ed6d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d749b2fa` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4c84c1e8` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-610b6a46` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3e494aea` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-156da191` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b8bedc73` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ecd7aa48` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b1c9760c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6eb3788b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd6ce692` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1234b5c5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-65f76244` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d633d5c0` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-093e65b5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1b330c84` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7f7c32f2` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f0902298` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dfd5b511` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bd4b301c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b48ddac3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4edf7db0` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a4070fe7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e11d031a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-19ba965b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-70519e22` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-811fedd3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dbd1d5d5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f507b4cb` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-59661477` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ca6894b3` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a43fd9e3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d05f6940` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e3c11469` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8375748d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-89fce443` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5bf41c06` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9fab67de` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9e8c7191` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b9e1b59e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1c1c6a97` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-de6d37c3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2eff298b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5fa89e36` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-defe909e` | 0.149 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0baab999` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3cae4b1a` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-456d4212` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-50d96c3a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-94ecc486` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e94700c3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a5bcabb9` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7691ad07` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9b7581d6` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b97d9a76` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce17a853` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e25fade6` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ff10098` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce84ebf8` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9f959114` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0cc85504` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b78f2d1d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0366844a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a0c8e4e8` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4c48a35a` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-de849603` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2832cbe5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6fad1c97` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-87dc66a2` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-af3885e9` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0d69fcb9` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fde1449f` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-27721a84` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4a97ab39` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e7d7abed` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-43815107` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bbc243c8` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c149a393` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3e00849b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4fb24628` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cd9ea55e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2feaa86a` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a2cc3a5` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-926de3ee` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c2a56aba` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-14c4ee74` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-258568c8` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c19f6022` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-69d36d73` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f3decd18` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fbfa3060` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a4b3596` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f4bc9e7b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1df3c4e7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2f572e34` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4b351d65` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-20a0a08d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5d42346d` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e91586d7` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-70957ac1` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a888a47e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-12a81195` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0259a48` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-af57e721` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b0e59d18` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-03c11fb7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-600c3db0` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3bc4bccf` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2ed0c4a5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3152a80f` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-719ec7e0` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a3cea54` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3346eabe` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dde90014` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-18993cad` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2a1d4375` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-91118cb3` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a0c0cd01` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6d581f53` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e3849891` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-26054c6b` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e04e00a5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7989c53d` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c208bd40` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5e134251` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e7462552` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-05f49e10` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0c836671` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8ad3bcc1` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-90025dd6` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6766751c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-461d7093` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5710fcd1` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fe7ceece` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7b19dd86` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bb76ff0e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-158ab2bb` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8caabd75` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-47dfc38c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-54509165` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ac538d04` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e4759f8c` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-edaef460` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-47df53de` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c9c21d5e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3614c5e5` | 0.149 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b72656cd` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-997695c0` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2d206cb7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-030fb2a7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-123a6683` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-739086fa` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9e409862` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3851bbb7` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d081c174` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-73325e0e` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a5535160` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5b9cda94` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5070a738` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1d050652` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2c7e61f2` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9a62ece5` | 0.149 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-21bab3ad` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2198cfb8` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0e282b1` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-31d0e122` | 0.148 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6752ac8d` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b6ed9f07` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7c98e07e` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c83130a4` | 0.148 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-55619eaf` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5751b94b` | 0.147 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7b235349` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-84193f60` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f159e9ed` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f1e22bb1` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ff669066` | 0.147 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4b4a88ba` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-be21cdfa` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c7a82b84` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-72d4fc8f` | 0.147 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86ab25bf` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-aa1816fe` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b77f6aad` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b7da3bb2` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-93da76b5` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a13bf61d` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1cf68c7f` | 0.147 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-42c38b80` | 0.147 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6d382627` | 0.147 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5182928b` | 0.147 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c79d65cd` | 0.147 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c53b40dc` | 0.147 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-81e3e2ba` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ed0f869d` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-45cf01e0` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-22e4e281` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e66caac8` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3013296e` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0200b90f` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-61323dff` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-058c9d3c` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-53987e52` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6e724f97` | 0.146 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd2f583b` | 0.146 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9b261dfc` | 0.146 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-728d2043` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-df9f4fbb` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-eb8a2bdc` | 0.145 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-149633f2` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-522d929b` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-67e6baa9` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-267bb590` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-944180e4` | 0.145 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-24f528a8` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4b41be48` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-769d81ba` | 0.145 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a97f37e9` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-13e9940d` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-92e9f910` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ff9b5c25` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-27fb306b` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cbbb2b7a` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-82b317df` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b303ac04` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f8868f72` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-057fdec6` | 0.145 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da9595fb` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-926e5f09` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f494c857` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e4ac990f` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e8c3f70b` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b588c5c7` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4de9cacb` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5064c0a9` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-47bfbbef` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c467dcb5` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4bbc024a` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a07a450` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8216918f` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f405ce72` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ab7ea2f` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ef329fb0` | 0.145 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5906dbf4` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f6ed79e3` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-92cc0d45` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f5b1d580` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fbf074d1` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ca393fdc` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4170230f` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bd02f8d9` | 0.145 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a9d35067` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c6c07d1e` | 0.145 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e1fcd348` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fa1f25cb` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-25d569de` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7c7df08d` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-67673f16` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d9a5492f` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3fe22492` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f2787eb2` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da33e242` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b0a9addd` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-01b1e80c` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd66cc47` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a5ccc3ae` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f83b877d` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-91127dd6` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c9686076` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd379736` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2f0c5955` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-82ca0355` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-739f831c` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d73c50f1` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-76dc5ba1` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dd35b815` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cc45ec44` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-eb30e49f` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-04808159` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-38029119` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6183911e` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-200e9630` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-344ba889` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-56c39ac6` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ed952d1c` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f70db55f` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-13be0169` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-df55a140` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-72434373` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-db7a8ec6` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8b34607` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e079b0d3` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7a8b3e64` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-33731d74` | 0.144 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-26aaf3f0` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7340d31f` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d4d2efcb` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b44ef513` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-69c198f0` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9a17d8c3` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-df0d419a` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2fa55c42` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-52d6dd7f` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-31f67fe1` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6bab136d` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0336a6b0` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6da8a413` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6166f2c3` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a75e3df1` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-409623bd` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e96821a4` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-818f247a` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d30ad28b` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e3d066f6` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9b356254` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-809c8b49` | 0.143 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-44fc0e5a` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b66d3382` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a086fe80` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f9488462` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-58ecccfe` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ea5ef98c` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1a174347` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1e932d1a` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1b7249dd` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7e179860` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a79bc28` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-47a59fc1` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5c0e1a0a` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-26526802` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-50078537` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5d0ad279` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fcd3f992` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1b429692` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2b2398ec` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-376070a6` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ac659a5` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b2051427` | 0.142 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7c5280e8` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bae50f99` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3a19fbf9` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ae10f79e` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d461560d` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b38de5ae` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4a9cb90f` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1de3833c` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-88ebd5b8` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fb90ad2a` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d25739a8` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8d32528` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-68308978` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-de168b6d` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1ac80818` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-55662594` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1a2ce245` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-962f430d` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c75cd4bc` | 0.142 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3ee44b63` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a3532cb8` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c270ebd8` | 0.142 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4a14585d` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d9db421d` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2c91a7ac` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f9501a77` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1285ca3e` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-24bc9efb` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-406ba3f5` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-46398720` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2240ef46` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-05dae6a9` | 0.141 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-260e7059` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d9bcbef7` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-18c33042` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-644e8740` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f10738e8` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e77ff54b` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2033bb65` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ef64b80` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ac99457f` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ba9b087` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ec23e008` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-06f01975` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a5eb83c9` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ad0a4d41` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-186b295d` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a647eb5` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3e4bad19` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e313a8b2` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a6277b11` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-73e898e4` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a5a52c17` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c7f6b380` | 0.141 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fdc48ffa` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0ff2d7a6` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6ef81bb8` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8657d79` | 0.141 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0788be17` | 0.141 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ca8c0112` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dca8e677` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9db5a7da` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9dfc291a` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-15b1cc9c` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b4ac501a` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8000f7c8` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1a2d0c15` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-31d3fbaa` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6aae297f` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-84cf0c6c` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b3673e88` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-be54423e` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1dc6bc32` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-30501451` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d18e99d2` | 0.141 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-241b8965` | 0.141 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1c540817` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cab86529` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b38b9279` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e55c82cf` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d99dc93f` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7510b18e` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-780debc3` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a36c6c6e` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-38553624` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b750c602` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-43188b54` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-11aa0529` | 0.140 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-53eaa077` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-72e15272` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-53759b67` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da80c46c` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-74334872` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1075abf0` | 0.140 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a89ad5d8` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86e65445` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7b69f9c9` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-949b141d` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8fe1f771` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3effb1dc` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-71753a08` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bf6d0d70` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a2e3ae38` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-03bf11c2` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c8b81183` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-61d0852c` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ca3d4a6b` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c461a113` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-280f31c8` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dbabe3aa` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8be49b47` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-80fe1a77` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9ed9170d` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4ec7e40d` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7ee38747` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f26b5028` | 0.139 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0a5144c3` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9077b494` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7dd51ee4` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86238d1e` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-13c7dc46` | 0.138 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8df27a91` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a1c361f4` | 0.138 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-217b7c78` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3d2794fb` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-34004c12` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d8de7a75` | 0.138 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6c152507` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a9044e2c` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d180d288` | 0.138 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ff6afff6` | 0.138 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-515ce32d` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-02c16198` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5724b53e` | 0.138 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bcf5ec3f` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e1747c34` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-97c64472` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3d677fcd` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-96723dd3` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8b690c54` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fa74e03b` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-101a6d71` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-80e6d59c` | 0.138 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fb8aaac5` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1df2bada` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fd423b29` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-458db087` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-896f3d65` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1e3dddab` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-adbad12b` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0dfbe070` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0c961739` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b12e3f04` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2a876d8a` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-281b29ce` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5457f394` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9501d319` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-818582a5` | 0.137 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ee18a226` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-90f3f838` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b367debd` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bc0176ff` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7efd53f8` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9ed7da3b` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b249882f` | 0.137 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0fc542b5` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5e3ecbdd` | 0.137 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-75df9046` | 0.137 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-982783af` | 0.137 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9bbc9566` | 0.137 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4de0a86d` | 0.137 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1fc4f3dd` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dfcc7e0d` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-befec73c` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-105d1679` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-68ce5371` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8b31ec87` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0c31538a` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9179f089` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c1f1f18e` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d5234cdc` | 0.136 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0fd79d67` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bfc79905` | 0.136 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e191838e` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-76f2158d` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a3265594` | 0.136 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-11f476f4` | 0.136 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-83ce0cea` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c7de934f` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-51e09137` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-30ae7b2a` | 0.136 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e2eb87b6` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-366502de` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bc9c67d3` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4926ab2c` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-63d584d5` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3a8fa5b1` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-af2a57eb` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7cc255f0` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-deedf9dd` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c163f2e6` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-583b6151` | 0.135 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cb100e90` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a4290b71` | 0.135 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e29ffab8` | 0.135 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c9945af6` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4bc8a7a2` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6159b95b` | 0.135 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8eceacaf` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fceb9bcc` | 0.135 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1a73906f` | 0.135 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a41dc3ed` | 0.135 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f23af0f1` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9b28be80` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-02b52e5b` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2fa25fee` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-928f0ce3` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-24825bfb` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0c638c88` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a7b63d96` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fea45f54` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3cb582ee` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8647113f` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0eccf2b4` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-79264973` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d535eeec` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-71b33378` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b657a1e2` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d422c5a9` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4bd3b253` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-eea9b11b` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-418b258e` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4fec0494` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f0aad56e` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b8f22709` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b288ec96` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-fcb8859a` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-20e088d8` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-530ed557` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-aa7cf6db` | 0.134 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-29fba8af` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-698c0e44` | 0.134 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b29278e3` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f62de453` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4c1708dc` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-abc9f7d5` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d6e4138e` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-293dbccc` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-47e7ec64` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b5228641` | 0.134 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-804aca36` | 0.134 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3ade011c` | 0.133 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-14f4b0eb` | 0.133 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a073b2d8` | 0.133 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a7c42072` | 0.133 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-031e7c04` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c8655038` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d3e917ce` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b227ba87` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a9ff548b` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0204cae` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86462e93` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-dff4e872` | 0.132 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c94cfd82` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8798ecd3` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e873d3aa` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b73275c8` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3efb29d0` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6e483022` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2eabeec3` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4012cd3d` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e846c5d0` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0f854037` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-888e0033` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-18a3e6bc` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-70cdf499` | 0.131 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a284cf76` | 0.131 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0b054a60` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a402da69` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-21cd95be` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5320c5ab` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2892af2e` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-bed39f89` | 0.131 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da4026d2` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-53c7ab00` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5582555c` | 0.131 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d662028c` | 0.131 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-07590946` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ce76a4e3` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-86c3c949` | 0.131 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9579b548` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3fc2fe7c` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6c3e5034` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d170a7e9` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6d9e7db0` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-783cba50` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d800c0b4` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c916f049` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0f59e5c1` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-693fd5d6` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cef1a2a9` | 0.131 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-93eea7de` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-02e747d6` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cea6682c` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0293f857` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-15d06aa2` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cdd8d229` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c389db24` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b5c8e753` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-632da924` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f1713f40` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f2788b47` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-646d8caf` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-15ae6c85` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2d6f8680` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d7091f08` | 0.130 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-5fcba512.removing-ec201e2d085b1894` | 0.130 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-2142d8e5` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-37ad6b6c` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f450c05a` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-5a2e33f1` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-6141b0b0` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d149ce14` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-7ee266d3` | 0.130 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-de732954` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8bc1672b` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-71a5997b` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e41a7005` | 0.130 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-79ee81f1` | 0.129 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-96d373ab` | 0.129 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-d493e467.removing-8302484fa7c3c828` | 0.129 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-9888335c.removing-c972aef0726f2b9a` | 0.129 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-ffc7e9ad.removing-79405ad685d2cf98` | 0.128 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-608ed47c.removing-07bc3fac8dc6f57f` | 0.128 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.card-task-fa69124f.removing-e8a88f3848672c76` | 0.128 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-52bffc69` | 0.125 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3aebd909` | 0.121 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1b2e83e3` | 0.119 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b3777342` | 0.119 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4e6ed8fa` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-13c90416` | 0.117 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-9e98878b` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a946111c` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1d3e0d92` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ffc43849` | 0.117 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-044f2398` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c37846cc` | 0.117 | Failed | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-da6e8e47` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8a7d4d16` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a87b9e00` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-b01f2e7b` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e116f6ae` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-402bbca2` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-63057c79` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-f8613557` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a30c729b` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-76611588` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a2601af5` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-245a0a09` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4f95b2ea` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-02e5b9b7` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ed41cbac` | 0.117 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-ba64b974` | 0.105 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-8ec6d6ff` | 0.105 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d9b74136` | 0.105 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-4407eec6` | 0.105 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-a08e6d8d` | 0.102 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cbe99e53` | 0.086 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-0f9c42e4` | 0.073 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cd2866b8` | 0.072 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-3fd04220` | 0.071 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-d72a027c` | 0.071 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-1893101c` | 0.071 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-e6f03caf` | 0.071 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-cf18faac` | 0.071 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-570fd325` | 0.058 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-18d59e3e` | 0.035 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-157eb737` | 0.009 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-c0b9f2f7` | 0.009 | Canceled | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-704c7117` | 0.006 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\card-task-66eec4dd` | 0.006 | Succeeded | â€” | unregistered/unknown | candidate | stale Antiphon artifact |
| `C:\Antiphon\worktrees\.antiphon` | 0.002 | non-task folder | â€” | unregistered/unknown | no/unknown | unknown |
| `C:\Antiphon\worktrees\card-task-1d5f5588` | 0.002 | Queued | â€” | unregistered/unknown | no/unknown | data to keep |
| `C:\Antiphon\worktrees\card-task-73233c64` | 0.002 | Queued | â€” | unregistered/unknown | no/unknown | data to keep |

Whole-tree reclamation has no safe unconditional command established by this inventory. Existing review front door (not run): `pwsh -NoProfile -File C:\src\Antiphon\scripts\worktree-residue.ps1 -Action Preview -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -Token $env:ANTIPHON_TASK_TOKEN`. This previews; it does not reclaim. A real release needs the full task ID, fresh revision, source SHA, report custody and explicit no-further-use authority (`scripts/worktree-residue.ps1:56-72`). SourceLanding tasks use their separate `delegate.ps1 -CleanupVerification <full-guid>` custody route after those checks; do not force-remove partial `.removing-*` remnants.

### Large logs

No `.log` or `.jsonl` over 500 MiB was found in the traversed content; unmeasured roots remain unchecked.

| File | GB | Class | Human command |
| --- | --- | --- | --- |

Current supervisor session-runner.log: 0.080 GB (79,505,923 bytes). No file content read. `docs/logs.md:13-17` distinguishes this unrotated supervisor output from the bounded runner Serilog logs.

## 4. Caches, profiles and system data

| Item | GB | Class | Human reclaim command (not run) |
| --- | --- | --- | --- |
| `C:\Users\lndco\.nuget\packages` | unknown / excluded | regenerable cache | `dotnet nuget locals global-packages --clear` |
| `C:\Program Files\dotnet\sdk` | 4.490 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files\dotnet\packs` | 1.201 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files\dotnet\sdk-manifests` | 0.001 | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.dotnet` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\npm-cache` | unknown / excluded | regenerable cache | `npm cache clean --force` |
| `C:\Users\lndco\.pnpm-store` | unknown / excluded | regenerable cache | `Remove-Item -LiteralPath 'C:\Users\lndco\.pnpm-store' -Recurse -Force` |
| `C:\Users\lndco\AppData\Local\pnpm\store` | unknown / excluded | regenerable cache | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\node\corepack` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\ms-playwright` | unknown / excluded | regenerable cache | `npx playwright uninstall --all` |
| `C:\Users\lndco\AppData\Local\pip\cache` | unknown / excluded | regenerable cache | `python -m pip cache purge` |
| `C:\Users\lndco\.gradle` | absent (metadata check) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.m2` | absent (metadata check) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Temp` | 168.112 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Windows\Temp` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\temp` | 0.955 (partial) | unknown | â€” retain / ownership decision required |
| `C:\tmp` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\.claude\projects` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.codex\sessions` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\.codex\log` | absent (metadata check) | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\Dropbox` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\OneDrive` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\edge-cdp` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\chrome-cdp` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Microsoft\Edge\User Data` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Google\Chrome\User Data` | unknown / excluded | unknown | â€” retain / ownership decision required |
| `C:\Windows\SoftwareDistribution` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\Windows\WinSxS` | unknown / excluded | data to keep | â€” retain / ownership decision required |
| `C:\System Volume Information` | unknown (access denied) | unknown | â€” retain / ownership decision required |

Installed SDKs observed with `dotnet --list-sdks`: 2.1.818, 5.0.408, 6.0.202, 6.0.302, 6.0.428, 7.0.317, 8.0.131, 9.0.100-rc.2.24474.11, 9.0.318, 10.0.300. SDK/workload files are installed toolchains, not proven unused caches. npm and pip reported the cache paths listed above. **Probe exception:** `pnpm store path` invoked Corepack, announced an automatic pnpm 12.11.2 acquisition, and failed with MODULE_NOT_FOUND. No further pnpm shim invocation was made; zero cache-side changes from that probe cannot be guaranteed. vssadmin list shadowstorage failed for lack of elevated privileges; System Volume Information size is unknown. No elevation, deletion, DISM servicing, hibernation change or pagefile change was attempted.

| System file | GB | Exact bytes |
| --- | --- | --- |
| `C:\hiberfil.sys` | 13.719 | 13719257088 |
| `C:\pagefile.sys` | 45.448 | 45447614464 |
| `C:\swapfile.sys` | 0.285 | 285212672 |

Cloud reparse paths were not followed or hydrated. Dropbox/OneDrive logical placeholder sizes do not establish physically reclaimable bytes; keep synchronized data. Browser profiles contain user state; clear selected cache through the browser only after owner review, never delete an entire profile as a cache. Claude projects and Codex sessions/logs are retained conversation data. Codex rollout deletion must use the CLI-owned path (`scripts/cleanup-codex-test-residue.ps1:3-15`), and this inventory did not inspect/classify thread contents.

Cloud roots were not reached; zero cloud-specific exclusions in this sample does not establish coverage.

| Largest-coverage cloud exclusions (first 30 paths) | Attributes |
| --- | --- |

## 5. Twenty largest files outside Windows and Program Files

| File | GB | Exact bytes | Class | Human command |
| --- | --- | --- | --- | --- |
| `C:\Users\lndco\AppData\Local\Docker\wsl\disk\docker_data.vhdx` | 62.968 | 62968037376 | unknown | â€” ownership/data-retention review required |
| `C:\pagefile.sys` | 45.448 | 45447614464 | unknown | â€” ownership/data-retention review required |
| `C:\hiberfil.sys` | 13.719 | 13719257088 | unknown | â€” ownership/data-retention review required |
| `C:\Users\lndco\AppData\Local\Packages\CanonicalGroupLimited.UbuntuonWindows_79rhkp1fndgsc\LocalState\ext4.vhdx` | 5.603 | 5602541568 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Plugins\rcp-be-lol-game-data\default-assets2.wad` | 3.423 | 3422505837 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Plugins\rcp-be-lol-game-data\default-assets.wad` | 2.669 | 2669124810 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Game\DATA\FINAL\Maps\Shipping\Map22.wad.client` | 2.532 | 2531803923 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Game\DATA\FINAL\Maps\Shipping\Map11.wad.client` | 2.450 | 2450480682 | unknown | â€” ownership/data-retention review required |
| `C:\ProgramData\VMware\VDM\Dumps\vmware-view-45556.dmp` | 1.512 | 1512155495 | unknown | â€” ownership/data-retention review required |
| `C:\Python310\Lib\site-packages\~orch\lib\torch_cuda.dll` | 1.390 | 1389687808 | unknown | â€” ownership/data-retention review required |
| `C:\Python310\Lib\site-packages\~-rch\lib\torch_cuda.dll` | 1.390 | 1389687808 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Game\DATA\FINAL\Maps\Shipping\Map12.wad.client` | 1.133 | 1132547789 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Game\DATA\FINAL\Companions.wad.client` | 1.113 | 1113403920 | unknown | â€” ownership/data-retention review required |
| `C:\Riot Games\League of Legends\Game\DATA\FINAL\Global.wad.client` | 0.975 | 974641936 | unknown | â€” ownership/data-retention review required |
| `C:\src\markdown-package\.spike-tauri\app\src-tauri\target\debug\mdpkg_spike_lib.lib` | 0.802 | 802228132 | unknown | â€” ownership/data-retention review required |
| `C:\src\markdown-package\.spike-tauri\app\src-tauri\target\debug\deps\mdpkg_spike_lib.lib` | 0.802 | 802228132 | unknown | â€” ownership/data-retention review required |
| `C:\Antiphon\worktrees\card-task-0726e42d\src\desktop\src-tauri\target\debug\deps\mdpkg_viewer_lib.lib` | 0.744 | 743783470 | unknown | â€” ownership/data-retention review required |
| `C:\Antiphon\worktrees\card-task-e3922461\src\desktop\src-tauri\target\debug\deps\mdpkg_viewer_lib.lib` | 0.743 | 742697550 | unknown | â€” ownership/data-retention review required |
| `C:\ProgramData\VMware\VDM\Dumps\vmware-view-9648.dmp` | 0.718 | 717968556 | unknown | â€” ownership/data-retention review required |
| `C:\plex-movies\Pan Tadeusz.avi` | 0.712 | 711567360 | data to keep | â€” ownership/data-retention review required |

## 5. Twenty largest folders overall

Ranked accessible logical folder totals, excluding the volume root; nested parents intentionally overlap. Protected/excluded trees cannot be ranked accurately.

| Folder | GB | Class | Human command |
| --- | --- | --- | --- |
| `C:\Users` | 239.587 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local` | 239.584 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData` | 239.584 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco` | 239.584 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Antiphon` | 191.330 | unknown | â€” retain / ownership decision required |
| `C:\Antiphon\worktrees` | 182.365 | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Temp` | 168.112 (partial) | unknown | â€” retain / ownership decision required |
| `C:\Program Files (x86)` | 124.001 (partial) | data to keep | â€” retain / ownership decision required |
| `C:\Program Files (x86)\Steam` | 106.541 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files (x86)\Steam\steamapps` | 105.307 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files (x86)\Steam\steamapps\common` | 105.283 | data to keep | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Docker` | 63.305 | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Docker\wsl` | 63.077 | unknown | â€” retain / ownership decision required |
| `C:\Users\lndco\AppData\Local\Docker\wsl\disk` | 62.968 | unknown | â€” retain / ownership decision required |
| `C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING\Game` | 54.962 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files (x86)\Steam\steamapps\common\ELDEN RING` | 54.962 | data to keep | â€” retain / ownership decision required |
| `C:\Program Files` | 52.077 (partial) | data to keep | â€” retain / ownership decision required |
| `C:\src` | 40.627 | unknown | â€” retain / ownership decision required |
| `C:\Riot Games` | 40.078 | unknown | â€” retain / ownership decision required |
| `C:\Riot Games\League of Legends` | 39.276 | unknown | â€” retain / ownership decision required |

## Evidence and rerun

Metadata-only local evidence root: `C:\Users\lndco\AppData\Local\Temp\antiphon-disk-4e06595f`. Generated payloads remain outside Git. These files contain path/size/status metadata, not payload contents.

| Evidence file | SHA-256 |
| --- | --- |
| `scan.py` | 19ae395efcbc8d46bec76b3b9410ae96eec2083168806c4b676dc374dd313317 |
| `primary.json` | bb8b4c6809e072746d662dcad8b150d3b7d265752c53c01060b2adb9369f143d |
| `sample.py` | db106c35c07657753b3ae4057b9e9101615e6fd54a2143f5cec837cf930a8646 |
| `sample.json` | 7c55f3dbea21ef4c0fd211b868f3cc6f7deb3e1d370cde0825a17dc77c8db170 |
| `combine.py` | 19777f6b0233971649b6d74a994c95be0bc6f9c945ce40824fdec18add86716b |
| `scan.json` | b68267a4583a14b866bd5488fe3f33dea680451221bf9106a7bcacf0197bf3b6 |
| `tasks.py` | 28030bf3aff5463c3d98bbbd58450f0077e6515e1b079a7de02c4ccba1c00a8c |
| `tasks.json` | 1f696f0478c527628429ad8443149309a4513ab2ba7cb06d5bdce0c41b33b500 |
| `git-worktrees.txt` | 701fd99a1c1c0a897a5b6443dd82aa69fe7d3ddeca2a30f8616171d21b71f02a |
| `merged-branches.txt` | 22c9e6e5cdb07b10c8d974c44f5e984348c4bd3ac0a1428d479609829fe7d419 |
| `docker-summary.jsonl` | 10ef2fbc48ab787f2e69ee21bb0887b80ab1112a000e7ad7d298256cdc13e954 |
| `docker-detail.txt` | 335db235e06dc682cefd2c0f2a8d2172189a3bce9e750b05261e6b2c7e6930e3 |

Rerun commands (inventory only):

```powershell
# The original whole-C walk used its complete 15-minute allowance.
# For uncovered areas, commission a targeted scan rather than repeating Antiphon.
python 'C:\Users\lndco\AppData\Local\Temp\antiphon-disk-4e06595f\scan.py'
python 'C:\Users\lndco\AppData\Local\Temp\antiphon-disk-4e06595f\tasks.py'
docker context show
docker system df -v
docker system df --format '{{json .}}'
git -C C:\src\Antiphon worktree list --porcelain
git -C C:\src\Antiphon for-each-ref --merged origin/master --format='%(refname:short)' refs/heads
wsl --list --verbose
Get-Volume -DriveLetter C
```

Task endpoint evidence: 1,003 HTTP 200 lookups at `http://localhost:17202/api/agent-tasks/<8-hex>`; status counts and every directory mapping are above and in tasks.json. Task token was passed from the inherited environment without printing or persisting it. File:line references support output/retention mechanics; disk and task-state conclusions come from measured metadata/API rows. No bug mechanism was established by this usage request.

Access-error sample (first 30; errors do not imply zero size):

| Path | Error | Windows error |
| --- | --- | --- |
| `C:\$Recycle.Bin\S-1-5-18` | PermissionError | 5 |
| `C:\$Recycle.Bin\S-1-5-21-212254706-463568577-1981337696-1000` | PermissionError | 5 |
| `C:\Program Files\Windows Defender Advanced Threat Protection\Classification\Configuration` | PermissionError | 5 |
| `C:\Program Files\WindowsApps` | PermissionError | 5 |
| `C:\Program Files (x86)\Dropbox\CrashReports` | PermissionError | 5 |
| `C:\Program Files (x86)\Google\CrashReports` | PermissionError | 5 |
| `C:\ProgramData\DockerDesktop\tmp-d4w` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Crypto\PCPKSP\WindowsAIK` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Crypto\RSA\S-1-5-18` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Crypto\SystemKeys` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Diagnosis` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\MapData\mapscache\BitsTemp` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\NetFramework\BreadcrumbStore` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Network\Downloader` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Provisioning\AssetCache` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Search\Data` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\SmsRouter` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Spectrum` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\AppRepository` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\Hyper-V` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\LfSvc` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\Models` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\PackagedEventProviders` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\Power Efficiency Diagnostics` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\SystemData` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_5319275A.WhatsAp_18ac1043b92e21a45965246861616edd554e19d_7ad25bc8_fc56ec43-7bbe-434f-b682-d809aa06ae81` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_Antiphon.Messagi_b22e36c69aaef6f6f25866882bc0c7bcaf01169_db607655_0ab3d995-126f-4302-ba02-bedd36901c7b` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_AUDIODG.EXE_9399734cfd989845c2e16696d1f1019b7fc962a_0532ff4e_9a01592e-b8a6-450d-93f4-08ef11767ed9` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_AUDIODG.EXE_a58b7870968eb3c32ee6746709ca2934b9cb75a_0532ff4e_b0a0ed2c-32a0-4a1c-bc90-c720ae188e17` | PermissionError | 5 |
| `C:\ProgramData\Microsoft\Windows\WER\ReportArchive\AppCrash_bad_module_info_51754d3881ae22b633a1547e4cdd56226cc5_85207d7d_305015fd-6f1c-46ce-94f8-1942f91fdae4` | PermissionError | 5 |

**Remaining uncertainties:** allocated and deduplicated physical totals outside VHDX; protected Windows/SVI contents; cloud excluded roots; live changes during scan; branch containment after the saved remote-ref snapshot; precise evidence/dirty/live-use custody for terminal worktrees; Docker VHDX shrink after any future cleanup. Resolve custody before selecting deletions.

**Not done, noted:** The operator can choose selected cache reclamation or commission separate worktree-retention/custody work; no cleanup or fix was designed or implemented.

--- next stage ---
next: decide
handoff: Review the measured disk inventory, rank selected cache/artifact candidates, and choose any separately authorized cleanup after current producer and worktree/evidence custody checks.
artifact: docs/investigations/2026-10-13-desktop-disk-usage.md
