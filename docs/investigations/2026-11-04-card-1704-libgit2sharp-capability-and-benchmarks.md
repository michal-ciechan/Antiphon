# CARD-1704 angle B: LibGit2Sharp capability, packaging and performance

Date: 2026-11-04. Task 4a75ce55 (Investigate, read-only angle B of three). Base master `1470340559889c2985ea722173181ea25c51b47c`.
Measured on server2 (Debian 12 bookworm, glibc 2.36, 24 cores, git 2.47.3, .NET 9.0.22) while it was loaded (load average 19-29).
Source and raw output: [`2026-11-04-card-1704-libgit2sharp-bench/`](2026-11-04-card-1704-libgit2sharp-bench/) (sources are stored as `.txt` so no
`.csproj` or `.cs` enters the repository build graph or source audits).

## Verdict in one paragraph

LibGit2Sharp 0.32.0 is usable on both of our platforms and **in-process is 10-100x cheaper for small ref and graph questions**
(rev-parse, show-ref, merge-base, is-ancestor, rev-list counts, ls-remote). It is **slower than git CLI for whole-tree work**
(status, merge/cherry-pick trees, worktree cherry-pick plus reset, enumerating all 5.7k refs). On Linux the process start floor is
about 1.3 ms, so the 152 git processes of one land cost well under a second there; the saving that motivates the card is on the
Windows desktop, where this repository already records at least 200 ms per git command under load. The land path therefore wants a
**hybrid**: a shared `Repository` for ref/graph reads and in-memory tree/commit writes, CLI kept for `rebase` in a worktree,
`worktree add/remove`, credentials-helper-backed network operations unless a token is provided, and anything that runs hooks.
The two things the brief expected libgit2 to lack turned out to be available: a **force-with-lease equivalent works** through the
push negotiation callback (reproduced), and cherry-pick and merge trees and commits are **byte-identical** to git's on the samples.

## 1. Package facts (NuGet metadata and nupkg contents, fetched 2026-11-04)

| Item | Value | Evidence |
|---|---|---|
| Latest stable LibGit2Sharp | **0.32.0**, published 2026-07-23/24 | `api.nuget.org/v3-flatcontainer/libgit2sharp/index.json` (0.32.0 last); GitHub release `0.32.0` 2026-07-23 |
| Native binaries | **LibGit2Sharp.NativeBinaries 2.0.324** (exact pin `[2.0.324]`), published 2026-07-20 | `LibGit2Sharp.nuspec` dependency groups |
| Embedded libgit2 | **1.8.6** (commit `5853918c`) | `libgit2/libgit2_hash.txt`; release body "security release that includes libgit2 v1.8.6"; version string `1.8.6` in the .so; runtime `GlobalSettings.Version` = `0.32.0+libgit2-5853918...` |
| Target frameworks | `net472`, `net8.0` (no net9.0 TFM; net8.0 assets load on net9.0) | `lib/net472`, `lib/net8.0`; the benchmark built and ran as `net9.0` |
| Licence | LibGit2Sharp MIT; libgit2 GPLv2 **with linking exception** (dynamic linking from proprietary code allowed) | `App_Readme/LICENSE.md`, `libgit2/libgit2.license.txt` |
| Compiled features | `Threads, Https, Ssh, NSec` | `GlobalSettings.Version.Features` at runtime |
| Maintenance | Low cadence: 0.30 (2024-11-26), 0.31 (2024-12-03), 0.32 (2026-07-23, security only). Repo not archived, 460 open issues. Upstream libgit2 has already shipped **1.8.7 and 1.9.7 (2026-08-13)** and `v2.0.0-rc.1` (2026-10-14); no LibGit2Sharp release carries them yet. | GitHub API `repos/libgit2/libgit2sharp`, `repos/libgit2/libgit2/releases` |

Native files in NativeBinaries 2.0.324 (`runtimes/<rid>/native/`): win-x64 `git2-5853918.dll` (2.0 MB), win-x86, win-arm64,
linux-x64 `libgit2-5853918.so` (2.0 MB), **linux-musl-x64**, linux-arm, linux-arm64, linux-musl-arm/arm64, linux-ppc64le,
osx-x64, osx-arm64. The linux-x64 .so links only `libc`, `libdl`, `libpthread` (`ldd`); HTTPS loads OpenSSL dynamically (the binary
names `libssl.so.3`, `.so.1.1`, `.so.1.0.0`, `.so.10`). SSH is libgit2's OpenSSH exec backend (`git_smart_subtransport_ssh_exec`),
so SSH still spawns `ssh`.

### What our images need

- Server image (root `Dockerfile:48`) and every runner target (`docker/session-runner-grok/Dockerfile`, `docker/tests/Dockerfile`)
  are `mcr.microsoft.com/dotnet/aspnet:9.0` / `sdk:10.0`, i.e. **Debian 12 bookworm, glibc**. The `linux-x64` RID asset applies; no
  musl concern. `libssl3` is already present in the aspnet image (.NET itself needs it), so HTTPS works without new packages.
- Nothing else to install. The pinned source-built git (CARD-1260, floor 2.43 in `docker/git-minimum-version.sh`) must stay,
  because the hybrid keeps the CLI.
- Windows desktop: `win-x64` asset; HTTPS uses WinHTTP (libgit2 default on Windows), so the Windows certificate store and proxy apply.

## 2. Capabilities and gaps against our land path

The land path (`server/Infrastructure/Git/LandingGit.cs`, `server/Application/Services/AgentTaskLandingProtocol.cs`) uses, by
literal count in `server/Infrastructure/Git/*.cs`: `rev-parse` 36, `worktree` 25, `show-ref` 20, `symbolic-ref` 14, `update-ref` 13,
`merge-base` 13, `status` 9, `push` 8, `ls-remote` 8, `fetch` 7, plus `rebase` (land: `AgentTaskLandingProtocol.cs:269`,
`-c rebase.autoStash=false -c rebase.updateRefs=false rebase <target>`), `cherry-pick`, `commit-tree`, `hash-object`, `gc`, `prune`.
(Angle A owns the full inventory; this table only judges libgit2 per verb.)

| Need | LibGit2Sharp 0.32 | Gap / note | Evidence |
|---|---|---|---|
| rev-parse, show-ref, symbolic-ref, update-ref | `Lookup`, `Refs[name]`, `Refs.UpdateTarget`, `Refs.Add(..., allowOverwrite)` with log message | `show-ref --exists` 0/1/2 distinction maps to null vs exception. Ref updates have no old-value CAS in the API (`git update-ref <ref> <new> <old>`); libgit2 `git_reference_set_target` is not conditional - the C API `git_reference_create_matching` is, but LibGit2Sharp does not import it. Keep `update-ref` with old value on the CLI or P/Invoke the native export. | `LibGit2Sharp.dll` imports `git_reference_create`, `_set_target`, no `*_matching`; the native .so exports `git_reference_create_matching` |
| merge-base, `--is-ancestor`, rev-list counts | `ObjectDatabase.FindMergeBase`, `CalculateHistoryDivergence`, `Commits.QueryBy` | none; results MATCH git | run output |
| cherry-pick `-x` | `Repository.CherryPick` (worktree) or `ObjectDatabase.CherryPickCommit` (in memory) | **no `-x`**: write `(cherry picked from commit <sha>)` ourselves. With the same message, committer and dates the in-memory commit is **byte-identical** to `git merge-tree` + `git commit-tree` (`763ea3b723f1` both). | run output |
| merge / merge-tree | `ObjectDatabase.MergeCommits`, `Repository.Merge` | tree MATCH `git merge-tree --write-tree` (`4d421a1b8157`) on the sample. libgit2's merge is not `ort`: no directory-rename detection; conflict sets can differ on rename-heavy changes. Built-in `union` driver exists (we use `merge=union` in `.gitattributes`); custom merge drivers are not run. | run output; `.gitattributes` |
| rebase in a worktree | `Repository.Rebase.Start/Continue/Abort` | Different engine from `git rebase` (merge backend + ort): empty-commit dropping, merge commits in the range, `rebase.updateRefs`, and autostash semantics are git-CLI features we pass with `-c` today. **Not proven equivalent**; keep CLI rebase or replace it with an explicit in-memory cherry-pick loop whose output we compare. | `AgentTaskLandingProtocol.cs:269` |
| fetch / push / ls-remote | `Commands.Fetch`, `Network.Push`, `Repository.ListRemoteReferences` | **No git credential helpers** (Git Credential Manager is not consulted): HTTPS needs a `CredentialsProvider` returning `UsernamePasswordCredentials` (token), which is a custody question for `docs/agent-credentials.md`. SSH goes through OpenSSH exec (still one `ssh` process; the credential callback is not used by that backend: to verify). No `--no-write-fetch-head` flag needed (libgit2 writes FETCH_HEAD; harmless). | features string; API |
| `--force-with-lease=<ref>:<expected>` | **Reproduced equivalent**: `PushOptions.OnNegotiationCompletedBeforePush` receives each `PushUpdate` with `SourceObjectId` = the remote's advertised value on this connection; returning `false` cancels (`UserCancelledException`, callback -7). The push command then carries that old value, so receive-pack refuses if the ref moved in between, exactly as the CLI lease does. Stale expectation refused, correct expectation pushed, leased delete pushed. Note the naming trap: `SourceObjectId` is the *remote old* value, `DestinationObjectId` the *new* one. | `LeaseProbe.Program.cs.txt`, lease output below |
| worktrees | `Repository.Worktrees` (list, lookup, `Add`, `Prune`, lock/unlock) over `git_worktree_*` (all present in the 1.8.6 binary) | No equivalent of our guarded removal flow, `worktree move`, `--orphan`, or `worktree repair`; `Prune` deletes metadata only. Keep CLI + `GuardedWorktreeRemoval`. | symbol list in the .so/.dll |
| status | `RetrieveStatus` | Slower than CLI (below). No untracked cache / fsmonitor. Does not write the index (so `GIT_OPTIONAL_LOCKS=0` is moot). | run output |
| config and `-c` | `Repository.Config` levels | `GIT_CONFIG_PARAMETERS` / `-c` are not honoured; libgit2 does not read `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE` etc. unless opened with `FROM_ENV`, which LibGit2Sharp does not do. Our `GIT_TERMINAL_PROMPT=0` is irrelevant (no prompting). | libgit2 docs |
| repository extensions | supported list is `noop, objectformat, worktreeconfig` | With `core.repositoryformatversion=1`, **`extensions.partialclone` and `extensions.refStorage` refuse to open** ("unsupported extension name"); `worktreeConfig` opens. The runner mirror `/work/repos/antiphon` is v1 with `remote.origin.promisor=true` and a `blob:none` filter but currently no `extensions.partialclone`; a real partial clone or a reftable repo cannot be opened. | probe 2 output |
| safe.directory / ownership | owner validation on by default (`GetOwnerValidation=True`), honours `safe.directory` from global/system config; `GlobalSettings.SetOwnerValidation(false)` is process-wide | Not reproduced (needs a foreign-owned repo). A Windows service account or an Administrators-owned checkout can differ from git CLI's own exemptions: verify on the desktop. No server code references `safe.directory` today. | `grep -rn safe.directory server src` empty |
| hooks | never run | We have no hooks (`/work/repos/antiphon/.git/hooks` only samples, no `core.hooksPath`) and no `commit.gpgsign`; nothing lost today, but CLI `rebase`/`commit` would start running hooks if someone adds them and libgit2 would not. | ls/grep |
| autocrlf / attributes / filters | built-in `text`, `eol`, `crlf`, `ident` filters honoured; custom `filter.<name>` drivers (for example LFS) not run | **Parity reproduced**: `* text=auto` plus `core.autocrlf` false/true/input, CRLF and LF files and a `-text` path: libgit2 `CreateBlob(stream, hintpath)` ids equal `git hash-object --path` in all 9 cases. Windows checkout direction (smudge to CRLF) not measured here. | probe 2 output |
| long paths (Windows) | `core.longpaths` is read (string present in the binary) | Not measurable on Linux; verify a >260 char path checkout and status on the desktop. | binary strings |
| thread safety | `Repository` is documented as not thread-safe; one handle per logical flow | 8 threads x 200 concurrent reads on one handle raised no exception here; that is **not** proof. Calls are synchronous and block a thread (network included); no `CancellationToken`: cancellation only through progress callbacks returning false, so today's per-command budget/kill becomes a callback-polled deadline. A native fault kills the server process instead of one git child. | probe output |
| disposal / handles | `Dispose` releases everything | After shared-handle work: 43 pack files mapped, 4 `.git` fds; **after Dispose: 0 and 0**. An **undisposed** handle still held 2 mapped packs and 1 fd **after a full GC** (no finalizer releases it). The mapped files are pack files in the common `.git/objects`; the 1-4 open `.git` fds were not classified. Likely consequence (not reproduced): no block on a worktree delete, but on Windows a leaked handle blocks `gc`/repack unlinking old packs. Verify on the desktop (section 3). | probe output |
| memory, 5.7k refs | working set 249 MiB, private 123 MiB, managed heap ~0 after all benchmarks on one handle | Working set is dominated by file-backed pack mappings (230 MiB of packs); private bytes are the real cost. | run output |

## 3. Measurements (server2, n=100 unless stated, milliseconds)

Scratch clone of the runner mirror (`git clone --no-hardlinks /work/repos/antiphon`; 181,969 objects, 230 MiB packs, 9,218 files at
HEAD) plus 5,000 `refs/bench/*` (5,753 refs total, packed - the mirror itself has 5,472 packed and 410 loose), a detached worktree
at `HEAD~10`. "shared" = one open `Repository` reused; "open+" = open, call, dispose each time.

| Operation | git CLI median / p95 | LibGit2Sharp median / p95 | Ratio |
|---|---|---|---|
| process floor `git --version` | 1.26 / 12.4 | - | - |
| open `Repository` + dispose | - | 0.18 / 0.24 | - |
| open + `Head.Tip.Sha` + dispose | - | 0.96 / 1.22 | - |
| rev-parse HEAD | 1.48 / 2.79 | shared 0.046 / 0.073 | 32x faster |
| show-ref --verify one ref | 2.19 / 3.38 | shared `Refs[name]` 0.021 / 0.034 | 100x faster |
| rev-list --count HEAD~200..HEAD (200 both) | 2.53 / 4.32 | `QueryBy().Count()` 1.07 / 2.09; `CalculateHistoryDivergence` 0.52 / 0.86 | 2.4-5x faster |
| merge-base HEAD~200 HEAD (same answer) | 4.07 / 6.08 | shared 0.49 / 0.77 | 8x faster |
| status --porcelain (clean, 9.2k files) | 49.0 / 136 | shared 61.2 / 165; open+ 66.9 / 155 | 1.25x **slower** |
| enumerate all 5,753 refs with targets | 11.9 / 72.4 | shared 54.8 / 95.0 (names only 148 under heavier load); `FromGlob("refs/heads/*")` 3.6 | 4.6x **slower** for all refs |
| in-memory cherry-pick tree + commit | merge-tree + commit-tree 10.3 / 13.8 | `CherryPickCommit` + `CreateCommit` 54.8 / 103 | 5x **slower** |
| true merge tree HEAD~10 HEAD | merge-tree 3.15 / 6.26 | `MergeCommits` 55.1 / 130 (68-89 with `FindRenames=false`/`SkipReuc`) | 17x **slower** |
| worktree cherry-pick + reset --hard | 110 / 157 | `CherryPick` + `Reset(Hard)` 389 / 598 | 3.5x **slower** |
| ls-remote GitHub public repo (n=20) | 449 / 557 | `ListRemoteReferences` 187 / 225 | 2.4x faster |

Semantic parity on the same inputs: rev-list count MATCH, merge-base MATCH, cherry-pick tree MATCH (`2a8aff18f9af`), cherry-pick
commit MATCH byte-identical (`763ea3b723f1`), merge tree MATCH (`4d421a1b8157`), worktree cherry-pick trees MATCH (commit ids differ
only because libgit2 writes no `-x` footer).

Likely reason libgit2 is slower on tree work (not profiled): git's `ort` and `merge-tree` skip unchanged subtrees by oid, libgit2 diffs whole trees; turning
rename detection off did not help (68-89 ms), so it is the tree walk, not renames. Ref enumeration in LibGit2Sharp lists names (`git_reference_list`, imported) and then looks up each
reference individually after `git_reference_foreach_name`.

### Lease push reproduction (local bare remote)

```
create expecting absent:            remote=000000000000 expected=000000000000 -> pushed
fast-forward expecting HEAD~2:      remote=ced59417e37b expected=ced59417e37b -> pushed
forced rewind, STALE expectation:   remote=de83206ea502 expected=ced59417e37b -> refused (callback -7), remote unchanged
forced rewind, correct expectation: remote=de83206ea502 expected=de83206ea502 -> pushed
leased delete:                      remote=3bbc8044f83c expected=3bbc8044f83c -> pushed, ref gone
```

### Break-even

- Linux: the CLI floor is ~1.3-1.5 ms per process under load. A shared handle costs ~1 ms to open and read the first ref, so it pays
  back on the **first** ref/graph call. But a whole land's 152 processes (commit `2b342761f`: "a land-path test starts 152 git
  processes instead of 174") are only ~0.2-0.6 s of spawn cost on server2; the land's time on Linux is in tree work, where libgit2 is
  slower. **On Linux the swap is not a performance win**; it is a test-seam and consistency win.
- Windows: not measurable from server2. Repository evidence: CARD-0788's investigation (`docs/superpowers/plans/2026-09-29-card-0788-...plan.md:42`)
  saw `GitCommands 10` time out a 2 s budget on the desktop, i.e. **at least 200 ms per git command under load**; the rc 0b05506b land
  E2E timeouts (card text) are in the same regime. Unloaded, Git for Windows commonly costs tens of ms per trivial command
  (CreateProcess, DLL load, Defender scanning of `git.exe`); this is an estimate, not measured here. At 20-200 ms per process, 152
  processes are **3-30 s per land**; replacing the ~100 small ref/graph calls with a shared handle removes most of that while the
  ~10-20 tree/network/worktree calls stay CLI.

### What the orchestrator should measure on the desktop (later, not during a qualification run)

```
pwsh -NoProfile -File docs/investigations/2026-11-04-card-1704-libgit2sharp-bench/run-desktop-bench.ps1 -Iterations 100
```

It clones the checkout into `%TEMP%\card1704-bench-<id>`, adds 5,000 packed refs and a worktree, builds the same benchmark through
`scripts/build-slot.ps1`, prints the same table plus CPU load and Defender exclusions, and names the scratch directory to delete.
Add on the desktop by hand: (1) a >260-character path checkout/status with `core.longpaths=true`; (2) open a checkout owned by
another account to see libgit2's ownership refusal versus git's; (3) try deleting a worktree while an undisposed `Repository` on it
is alive (expect success, since only common-dir packs are held) and run `git gc` while one is alive on the main repo (expect unlink
warnings).

## 4. Remaining uncertainties

1. Windows per-process cost and libgit2 tree-op cost on NTFS are estimates until the desktop script runs.
2. `Repository.Rebase` equivalence with `git rebase` (empty commits, merge commits in range, conflict sets) is unproven; only clean
   cherry-pick/merge samples were compared.
3. SSH exec backend credential behaviour, and HTTPS token custody without Git Credential Manager.
4. `safe.directory`/ownership behaviour for the desktop service account; Windows long paths; Windows CRLF checkout direction.
5. libgit2 thread-safety beyond "one handle per flow"; the concurrent-read probe saw no fault but proves nothing.
6. Security lag: LibGit2Sharp ships libgit2 1.8.6 while upstream fixed further issues in 1.8.7/1.9.7 (2026-08-13).

## Not done, noted

- Fix idea (one line, not designed): a hybrid `IGit` whose shared `Repository` serves ref/graph reads, in-memory cherry-pick commits
  with our own `-x` footer and lease pushes through the negotiation callback, with CLI kept for rebase, worktrees, status and whole-ref scans.
