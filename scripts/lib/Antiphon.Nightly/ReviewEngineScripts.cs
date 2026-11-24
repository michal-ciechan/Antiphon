// The review engine's operator commands (feature 012, docs/features/012-review-engine; unit 42 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). Each replaced the scripts/<name>.ps1 of the same name, deleted
// 2026-11-24 (nothing started them by path), with the same parameters in kebab case. The CLI runs them as
//
//   antiphon-scripts review-standalone [--port 17262] [--client-port 17263] [--postgres-port 17290] [--repo <owner/name>]
//       [--mirror-path <dir>] [--vault-item <name>] [--viewer-login <login>] [--poll-seconds 120] [--public-base-url <url>]
//       [--no-auto-merge] [--no-client] [--no-polling] [--stop]
//   antiphon-scripts review-github-setup [--vault-item <name>] [--repo <owner/name>] [--project <name>] [--public-base-url <url>]
//       [--viewer-login <login>] [--secrets-id <id>] [--no-auto-merge] [--dry-run]
//   antiphon-scripts review-live-github --pr <n> [--repo <owner/name>] [--vault-item <name>] [--required-label <label>] [--no-build]
//
// The three read the antiphon-review GitHub App from Bitwarden (ReviewAppVault): BW_SESSION from this process's environment, else
// ~/.bw-session (trimmed), then `bw get item <item> --nointeraction` with BW_SESSION in its environment; the item's "Client ID" and
// "Installation ID" fields and its notes (the PEM key). JSON property and field names match exactly.
//
// review-standalone: a review-only server (and its Vite client) on this machine, for the review flow without the AppHost (the
// laptop). Postgres is the container antiphon-review-standalone-pg on 127.0.0.1:<postgres-port> with its own volume (started, or
// run when missing; pg_isready within 60 s), never the dev stack's 17280. Reviewer worktrees come from a mirror clone at
// --mirror-path (cloned, else fetched with --prune); the main checkout is never used. The server is `dotnet run --project
// <checkout>/server --no-launch-profile` on --port, isolated like the test hosts (the session runner and the broker at dead
// addresses; Hangfire, the zombie census, schedules, card file sync and the worktree janitors off), with the review settings and
// the app's key in its environment only (nothing in user-secrets, which test hosts read). The client is `cmd.exe /c npx vite --host
// 127.0.0.1` in client/ on --client-port, without the key. Each starts detached in its own hidden console, its output in
// logs/review-standalone/{server,client}{,.err}.log; the pids go to logs/review-standalone/pids.json. --stop stops each recorded
// pid whose command line still names review-standalone, Antiphon.Server, vite or dotnet (taskkill /T /F) and removes the file.
// Ports 17202-17205 (the canonical stack) and 17280 are refused, and so is a second start while pids.json exists.
//
// review-github-setup: the app's ids and key, with the plain review settings, into the server's user-secrets (--secrets-id,
// antiphon-server) through `dotnet user-secrets set --id <id>`'s stdin: the key never reaches a command line, the console or a
// file. Existing user-secrets are kept (set merges). --dry-run prints the settings with the key redacted and writes nothing.
//
// review-live-github: tests/Antiphon.Tests/Review/ReviewLiveGitHubTests against a real scratch PR. `gh pr view` must list
// --required-label (antiphon:live-check) before the vault is read; then the app's ids and key go into the test run's environment
// only (ANTIPHON_LIVE_GITHUB_*; without them every test of the class skips) and `dotnet run --project tests/Antiphon.Tests
// [--no-build] -- --treenode-filter /*/*/ReviewLiveGitHubTests/*` runs in the checkout under the build slot (label
// review-live-github), in this process.
//
// Exit codes: review-standalone and review-github-setup 0 done (also --stop and --dry-run); review-live-github the test run's code
// (the build slot's: 4 no slot, 130 interrupted); 1 refused or failed (one stderr line) for each; usage 2 (NightlyUsageException).
// Every observation of the machine is the seams' (IReviewEngineSeams: tools run to their exit, detached starts, a process's command
// line, the tree stop, the build slot); the clock, the sleep and BW_SESSION are the host's; the files are IFileSystem's. Plain C#:
// no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>
    /// One tool run to its exit: the program (looked up on PATH), its literal argv, the variables added to its environment (null:
    /// none) and the text written to its stdin (null: none).
    /// </summary>
    public sealed record ReviewToolCall(string FileName, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment = null, string Input = null)
    {
        /// <summary>The program and the arguments, space separated.</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>A tool's exit code, its stdout and its stderr.</summary>
    public sealed record ReviewToolResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// One detached child (Start-Process -WindowStyle Hidden -PassThru with both streams redirected): the program and its argv, the
    /// directory it starts in, its stdout and stderr files, and the variables changed in its environment (a null value removes one).
    /// </summary>
    public sealed record ReviewDetachedStart(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, string StandardOutput,
        string StandardError, IReadOnlyDictionary<string, string> Environment);

    /// <summary>What the review engine commands do to the machine. A test binds a fake.</summary>
    public interface IReviewEngineSeams
    {
        /// <summary>Runs the tool to its exit and returns its code and output. Throws when it cannot start.</summary>
        ReviewToolResult Run(ReviewToolCall call);

        /// <summary>Starts a detached child (its own hidden console) and returns its process id. Throws when it cannot start.</summary>
        int Start(ReviewDetachedStart start);

        /// <summary>Win32_Process's CommandLine of the process; null when there is no such process or it cannot be read.</summary>
        string CommandLine(int pid);

        /// <summary>taskkill /PID &lt;pid&gt; /T /F (best effort).</summary>
        void StopTree(int pid);

        /// <summary>The build slot of an in-process caller on this process's console: the command's exit code.</summary>
        int BuildSlot(BuildSlotOptions options);
    }

    /// <summary>The review-standalone command's options (review-standalone.ps1's parameters).</summary>
    public sealed class ReviewStandaloneOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "review-standalone [--port 17262] [--client-port 17263] [--postgres-port 17290] [--repo <owner/name>] [--mirror-path <dir>]"
            + " [--vault-item <name>] [--viewer-login <login>] [--poll-seconds 120] [--public-base-url <url>] [--no-auto-merge] [--no-client] [--no-polling] [--stop]";

        /// <summary>The server's port.</summary>
        public int Port { get; init; } = 17262;

        /// <summary>The Vite client's port.</summary>
        public int ClientPort { get; init; } = 17263;

        /// <summary>The Postgres container's host port.</summary>
        public int PostgresPort { get; init; } = 17290;

        /// <summary>The repository reviewed, polled and mirrored.</summary>
        public string Repo { get; init; } = ReviewAppVault.DefaultRepo;

        /// <summary>The mirror clone reviewer worktrees are cut from.</summary>
        public string MirrorPath { get; init; } = @"C:\src\antiphon-review-mirror";

        /// <summary>The Bitwarden item that holds the app.</summary>
        public string VaultItem { get; init; } = ReviewAppVault.DefaultVaultItem;

        /// <summary>Review:DefaultViewerLogin.</summary>
        public string ViewerLogin { get; init; } = ReviewAppVault.DefaultViewerLogin;

        /// <summary>Review:Polling:IntervalSeconds.</summary>
        public int PollSeconds { get; init; } = 120;

        /// <summary>Where links in GitHub statuses and comments point (a trailing '/' dropped); empty: http://localhost:&lt;client-port&gt;.</summary>
        public string PublicBaseUrl { get; init; } = "";

        /// <summary>Keep the repo's merge gate on (the product default) instead of merging approved PRs here.</summary>
        public bool NoAutoMerge { get; init; }

        /// <summary>Start no Vite client.</summary>
        public bool NoClient { get; init; }

        /// <summary>Leave GitHub polling off (a manual POST /api/review-polling/poll still runs a pass).</summary>
        public bool NoPolling { get; init; }

        /// <summary>Stop what pids.json records instead of starting.</summary>
        public bool Stop { get; init; }

        /// <summary>The checkout: the server and client projects, and logs/review-standalone.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The home directory that holds .bw-session.</summary>
        public string Home { get; init; } = "";

        /// <summary>The temp directory the server's git workspace goes under ($env:TEMP).</summary>
        public string TempRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout, home and temp directory come from the CLI.</summary>
        public static ReviewStandaloneOptions Parse(IReadOnlyList<string> arguments, string repoRoot, string home, string tempRoot)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--port", "--client-port", "--postgres-port", "--repo", "--mirror-path", "--vault-item", "--viewer-login", "--poll-seconds", "--public-base-url" },
                new[] { "--no-auto-merge", "--no-client", "--no-polling", "--stop" });
            var defaults = new ReviewStandaloneOptions();
            return new ReviewStandaloneOptions
            {
                Port = flags.Int("--port", defaults.Port),
                ClientPort = flags.Int("--client-port", defaults.ClientPort),
                PostgresPort = flags.Int("--postgres-port", defaults.PostgresPort),
                Repo = flags.Value("--repo", defaults.Repo),
                MirrorPath = flags.Value("--mirror-path", defaults.MirrorPath),
                VaultItem = flags.Value("--vault-item", defaults.VaultItem),
                ViewerLogin = flags.Value("--viewer-login", defaults.ViewerLogin),
                PollSeconds = flags.Int("--poll-seconds", defaults.PollSeconds),
                PublicBaseUrl = flags.Value("--public-base-url", ""),
                NoAutoMerge = flags.Switch("--no-auto-merge"),
                NoClient = flags.Switch("--no-client"),
                NoPolling = flags.Switch("--no-polling"),
                Stop = flags.Switch("--stop"),
                RepoRoot = repoRoot ?? "",
                Home = home ?? "",
                TempRoot = tempRoot ?? "",
            };
        }
    }

    /// <summary>The review-github-setup command's options (review-github-setup.ps1's parameters).</summary>
    public sealed class ReviewGitHubSetupOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "review-github-setup [--vault-item <name>] [--repo <owner/name>] [--project <name>] [--public-base-url <url>] [--viewer-login <login>]"
            + " [--secrets-id antiphon-server] [--no-auto-merge] [--dry-run]";

        /// <summary>The Bitwarden item that holds the app.</summary>
        public string VaultItem { get; init; } = ReviewAppVault.DefaultVaultItem;

        /// <summary>The repository reviewed and polled.</summary>
        public string Repo { get; init; } = ReviewAppVault.DefaultRepo;

        /// <summary>Review:Agents:Repos:0:Project.</summary>
        public string Project { get; init; } = "Antiphon";

        /// <summary>Review:PublicBaseUrl.</summary>
        public string PublicBaseUrl { get; init; } = "https://antiphon.desktop.codeperf.net";

        /// <summary>Review:DefaultViewerLogin.</summary>
        public string ViewerLogin { get; init; } = ReviewAppVault.DefaultViewerLogin;

        /// <summary>The server's user-secrets id.</summary>
        public string SecretsId { get; init; } = "antiphon-server";

        /// <summary>Keep the repo's merge gate on (the product default) instead of merging approved PRs here.</summary>
        public bool NoAutoMerge { get; init; }

        /// <summary>Print the settings with the key redacted and write nothing.</summary>
        public bool DryRun { get; init; }

        /// <summary>The home directory that holds .bw-session.</summary>
        public string Home { get; init; } = "";

        /// <summary>The flags after the command name; the home directory comes from the CLI.</summary>
        public static ReviewGitHubSetupOptions Parse(IReadOnlyList<string> arguments, string home)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--vault-item", "--repo", "--project", "--public-base-url", "--viewer-login", "--secrets-id" },
                new[] { "--no-auto-merge", "--dry-run" });
            var defaults = new ReviewGitHubSetupOptions();
            return new ReviewGitHubSetupOptions
            {
                VaultItem = flags.Value("--vault-item", defaults.VaultItem),
                Repo = flags.Value("--repo", defaults.Repo),
                Project = flags.Value("--project", defaults.Project),
                PublicBaseUrl = flags.Value("--public-base-url", defaults.PublicBaseUrl),
                ViewerLogin = flags.Value("--viewer-login", defaults.ViewerLogin),
                SecretsId = flags.Value("--secrets-id", defaults.SecretsId),
                NoAutoMerge = flags.Switch("--no-auto-merge"),
                DryRun = flags.Switch("--dry-run"),
                Home = home ?? "",
            };
        }
    }

    /// <summary>The review-live-github command's options (review-live-github.ps1's parameters).</summary>
    public sealed class ReviewLiveGitHubOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "review-live-github --pr <n> [--repo <owner/name>] [--vault-item <name>] [--required-label antiphon:live-check] [--no-build]";

        /// <summary>The scratch PR's number.</summary>
        public int Pr { get; init; }

        /// <summary>The repository the PR is in.</summary>
        public string Repo { get; init; } = ReviewAppVault.DefaultRepo;

        /// <summary>The Bitwarden item that holds the app.</summary>
        public string VaultItem { get; init; } = ReviewAppVault.DefaultVaultItem;

        /// <summary>The label a scratch PR carries; the check posts nowhere else.</summary>
        public string RequiredLabel { get; init; } = "antiphon:live-check";

        /// <summary>Reuse the last build of tests/Antiphon.Tests instead of building first.</summary>
        public bool NoBuild { get; init; }

        /// <summary>The checkout the test project is run from.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The home directory that holds .bw-session.</summary>
        public string Home { get; init; } = "";

        /// <summary>The flags after the command name; the checkout and home directory come from the CLI. --pr is required.</summary>
        public static ReviewLiveGitHubOptions Parse(IReadOnlyList<string> arguments, string repoRoot, string home)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--pr", "--repo", "--vault-item", "--required-label" }, new[] { "--no-build" });
            var pr = flags.Required("--pr");
            if (!int.TryParse(pr, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                throw new NightlyUsageException("--pr expects a PR number, not '" + pr + "'");
            var defaults = new ReviewLiveGitHubOptions();
            return new ReviewLiveGitHubOptions
            {
                Pr = number,
                Repo = flags.Value("--repo", defaults.Repo),
                VaultItem = flags.Value("--vault-item", defaults.VaultItem),
                RequiredLabel = flags.Value("--required-label", defaults.RequiredLabel),
                NoBuild = flags.Switch("--no-build"),
                RepoRoot = repoRoot ?? "",
                Home = home ?? "",
            };
        }
    }

    /// <summary>A refusal or failure of a review engine command: its message is the one stderr line, the exit 1.</summary>
    public sealed class ReviewEngineFailure : Exception
    {
        /// <summary>A failure with the line printed.</summary>
        public ReviewEngineFailure(string message) : base(message) { }
    }

    /// <summary>The antiphon-review app's Bitwarden item, as read: its fields in order and its notes.</summary>
    public sealed class ReviewAppItem
    {
        private readonly string _vaultItem;
        private readonly IReadOnlyList<KeyValuePair<string, string>> _fields;

        private ReviewAppItem(string vaultItem, IReadOnlyList<KeyValuePair<string, string>> fields, string notes)
        {
            _vaultItem = vaultItem;
            _fields = fields;
            Notes = notes;
        }

        /// <summary>The item's notes ([string]$item.notes: empty when there are none).</summary>
        public string Notes { get; }

        /// <summary>
        /// Get-Field: the value of the first field with the name (matched exactly); ReviewEngineFailure ("Bitwarden item '&lt;item&gt;'
        /// has no '&lt;name&gt;' field.") when there is none or its value is empty.
        /// </summary>
        public string Field(string name)
        {
            foreach (var field in _fields)
            {
                if (!string.Equals(field.Key, name, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(field.Value)) break;
                return field.Value;
            }
            throw new ReviewEngineFailure("Bitwarden item '" + _vaultItem + "' has no '" + name + "' field.");
        }

        /// <summary>`bw get item`'s JSON; ReviewEngineFailure when it is not JSON (the text is never printed: it holds the key).</summary>
        public static ReviewAppItem Parse(string vaultItem, string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json ?? "");
                var root = document.RootElement;
                var fields = new List<KeyValuePair<string, string>>();
                var notes = "";
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("fields", out var list) && list.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var field in list.EnumerateArray())
                        {
                            if (field.ValueKind != JsonValueKind.Object || !field.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
                            fields.Add(new KeyValuePair<string, string>(name.GetString(), field.TryGetProperty("value", out var value) ? Text(value) : null));
                        }
                    }
                    if (root.TryGetProperty("notes", out var text)) notes = Text(text) ?? "";
                }
                return new ReviewAppItem(vaultItem, fields, notes);
            }
            catch (JsonException)
            {
                throw new ReviewEngineFailure("Bitwarden item '" + vaultItem + "' did not read as JSON.");
            }
        }

        // [string] of a JSON value: a string as is, a number or true as its text, null and false as nothing (a falsy value).
        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => null,
            JsonValueKind.True => "True",
            _ => value.GetRawText(),
        };
    }

    /// <summary>The Bitwarden read the three commands share, and their common defaults and helpers.</summary>
    public static class ReviewAppVault
    {
        /// <summary>The repository the review engine runs on.</summary>
        public const string DefaultRepo = "michal-ciechan/Antiphon";

        /// <summary>The Bitwarden item that holds the antiphon-review GitHub App.</summary>
        public const string DefaultVaultItem = "GitHub App - antiphon-review";

        /// <summary>The owner's GitHub login.</summary>
        public const string DefaultViewerLogin = "michal-ciechan";

        /// <summary>The refusal when there is neither BW_SESSION nor ~/.bw-session.</summary>
        public const string Locked = "Bitwarden is locked: no BW_SESSION and no ~/.bw-session.";

        /// <summary>
        /// The item: BW_SESSION from the host's environment, else the trimmed ~/.bw-session (lockedMessage when neither), then `bw get
        /// item &lt;item&gt; --nointeraction` with BW_SESSION in its environment (its stderr passed through). ReviewEngineFailure when bw
        /// fails or prints nothing ("Bitwarden item '&lt;item&gt;' not found or the vault is locked.") or the item is not JSON.
        /// </summary>
        public static ReviewAppItem Read(string vaultItem, string lockedMessage, string home, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            var session = host.Variables("BW_SESSION");
            if (string.IsNullOrEmpty(session))
            {
                var sessionFile = files.Path.Combine(home ?? "", ".bw-session");
                if (!files.File.Exists(sessionFile) && !files.Directory.Exists(sessionFile)) throw new ReviewEngineFailure(lockedMessage);
                session = files.File.ReadAllText(sessionFile).Trim();
            }
            var read = seams.Run(new ReviewToolCall("bw", new[] { "get", "item", vaultItem, "--nointeraction" },
                new Dictionary<string, string>(StringComparer.Ordinal) { ["BW_SESSION"] = session }));
            Pass(host.Error, read.Error);
            if (read.ExitCode != 0 || string.IsNullOrWhiteSpace(read.Output)) throw new ReviewEngineFailure("Bitwarden item '" + vaultItem + "' not found or the vault is locked.");
            return ReviewAppItem.Parse(vaultItem, read.Output);
        }

        /// <summary>Writes each line of a tool's stream (the console the script's tool wrote to); nothing for empty text.</summary>
        public static void Pass(TextWriter writer, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var count = lines.Length > 0 && lines[lines.Length - 1].Length == 0 ? lines.Length - 1 : lines.Length;
            for (var i = 0; i < count; i++) writer.WriteLine(lines[i]);
        }

        /// <summary>The stdout lines of a tool (native output as PowerShell reads it: split at line ends, the last empty one dropped).</summary>
        public static IReadOnlyList<string> Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines;
        }

        /// <summary>
        /// Runs a command body as the scripts ran under $ErrorActionPreference = 'Stop': a ReviewEngineFailure, or a file, process or
        /// JSON failure, is one stderr line and exit 1.
        /// </summary>
        public static int Guard(NightlyHost host, Func<int> body)
        {
            try
            {
                return body();
            }
            catch (Exception e) when (e is ReviewEngineFailure || e is IOException || e is UnauthorizedAccessException || e is Win32Exception
                || e is InvalidOperationException || e is JsonException)
            {
                host.Error.WriteLine(e.Message);
                return 1;
            }
        }
    }

    /// <summary>The review-standalone command (review-standalone.ps1's body).</summary>
    public static class ReviewStandalone
    {
        /// <summary>The Postgres container.</summary>
        public const string Container = "antiphon-review-standalone-pg";

        /// <summary>The Postgres container's volume.</summary>
        public const string Volume = "antiphon_review_standalone_pgdata";

        /// <summary>The server environment's key variable: the server alone gets it.</summary>
        public const string KeyVariable = "Review__Secrets__GitHubAppPrivateKey";

        /// <summary>How long Postgres has to answer pg_isready.</summary>
        public static readonly TimeSpan ReadyWithin = TimeSpan.FromSeconds(60);

        /// <summary>A recorded pid is stopped only while its command line still matches (-match: case-insensitive).</summary>
        public static readonly Regex Ours = new Regex("review-standalone|Antiphon.Server|vite|dotnet", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Starts the stack, or with Stop stops it (see the file header): 0 done; 1 after one stderr line.</summary>
        public static int Run(ReviewStandaloneOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return ReviewAppVault.Guard(host, () => Body(options, host, files, seams));
        }

        /// <summary>
        /// The server's environment, in the script's order (the vault's fields read in that order): the isolation, the database, the
        /// workspace under work, and the review engine. A null value removes the variable (the merge gate with NoAutoMerge).
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> ServerEnvironment(ReviewStandaloneOptions options, ReviewAppItem item, string work, IFileSystem files)
        {
            var o = options;
            var repo = o.Repo;
            return new List<KeyValuePair<string, string>>
            {
                Pair("ASPNETCORE_ENVIRONMENT", "Development"),
                Pair("ASPNETCORE_URLS", "http://127.0.0.1:" + Number(o.Port)),
                Pair("ConnectionStrings__DefaultConnection", "Host=127.0.0.1;Port=" + Number(o.PostgresPort) + ";Database=antiphon;Username=antiphon;Password=antiphon_dev"),
                // Isolation, as the test hosts do it (ProductionRunnerGuard): nothing here starts a process on a runner.
                Pair("SessionRunner__BaseUrl", "http://127.0.0.1:1"),
                Pair("SessionRunner__Enabled", "false"),
                Pair("Delegation__CheckInterpreterEnabled", "false"),
                Pair("Delegation__DiagnoseEnabled", "false"),
                Pair("Delegation__OutputDistillerEnabled", "false"),
                Pair("Hangfire__ServerEnabled", "false"),
                Pair("ZombieCensus__Enabled", "false"),
                Pair("Schedules__Enabled", "false"),
                Pair("CardFileSync__Enabled", "false"),
                Pair("WorktreeResidue__Enabled", "false"),
                Pair("WorktreeLegacyRelease__Enabled", "false"),
                Pair("RunnerWorktreeRetention__Enabled", "false"),
                Pair("AgentTui__ImportProfilesOnStartup", "false"),
                Pair("GitHub__Enabled", "false"),
                Pair("AntiphonMessaging__BootstrapServers", "127.0.0.1:1"),
                Pair("Git__WorkspacePath", work),
                Pair("Git__WorktreeBasePath", files.Path.Combine(work, "worktrees")),
                // The review engine.
                Pair("Review__CodeHosts__Hosts__github__Kind", "GitHub"),
                Pair("Review__CodeHosts__Hosts__github__App__AppId", item.Field("Client ID")),
                Pair("Review__CodeHosts__Hosts__github__App__InstallationId", item.Field("Installation ID")),
                Pair(KeyVariable, item.Notes),
                Pair("Review__PublicBaseUrl", string.IsNullOrEmpty(o.PublicBaseUrl) ? "http://localhost:" + Number(o.ClientPort) : o.PublicBaseUrl.TrimEnd('/')),
                Pair("Review__DefaultViewerLogin", o.ViewerLogin),
                Pair("Review__Policy__AuthorMayDecideRepos__0", repo),
                Pair("Review__Policy__GatesOff__merge-gate__0", o.NoAutoMerge ? null : repo),
                Pair("Review__Agents__Transport", "cli"),
                Pair("Review__Agents__Repos__0__Repo", repo),
                Pair("Review__Agents__Repos__0__LocalPath", o.MirrorPath),
                Pair("Review__Completions__Providers__haiku-cli__Dialect", "claude-cli"),
                Pair("Review__Completions__Providers__haiku-cli__Model", "haiku"),
                Pair("Review__Completions__Chains__completions-cheap__0__Provider", "haiku-cli"),
                Pair("Review__Decisions__Providers__haiku-cli__Dialect", "claude-cli"),
                Pair("Review__Decisions__Providers__haiku-cli__Model", "haiku"),
                Pair("Review__Decisions__Chains__decisions-default__0__Provider", "haiku-cli"),
                Pair("Review__Polling__Enabled", o.NoPolling ? "false" : "true"),
                Pair("Review__Polling__IntervalSeconds", Number(o.PollSeconds)),
                Pair("Review__Polling__Repos__0__Repo", repo),
            };
        }

        private static int Body(ReviewStandaloneOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            var root = options.RepoRoot;
            var logDir = files.Path.Combine(root, "logs", "review-standalone");
            var pidFile = files.Path.Combine(logDir, "pids.json");
            files.Directory.CreateDirectory(logDir);
            if (options.Stop) return StopStandalone(pidFile, host, files, seams);

            foreach (var port in new[] { options.Port, options.ClientPort, options.PostgresPort })
            {
                if (port >= 17202 && port <= 17205) throw new ReviewEngineFailure("Port " + Number(port) + " belongs to the canonical stack (17202-17205).");
                if (port == 17280) throw new ReviewEngineFailure("Port 17280 is the dev stack Postgres.");
            }
            if (files.File.Exists(pidFile) || files.Directory.Exists(pidFile)) throw new ReviewEngineFailure("Already running (see " + pidFile + "); use --stop first.");

            // Postgres: run the container when it is missing, start it when it is stopped, then wait for pg_isready.
            var state = seams.Run(new ReviewToolCall("docker", new[] { "inspect", "-f", "{{.State.Running}}", Container }));
            if (state.ExitCode != 0)
            {
                var created = seams.Run(new ReviewToolCall("docker", new[]
                {
                    "run", "-d", "--name", Container, "-p", "127.0.0.1:" + Number(options.PostgresPort) + ":5432", "-e", "POSTGRES_DB=antiphon", "-e", "POSTGRES_USER=antiphon",
                    "-e", "POSTGRES_PASSWORD=antiphon_dev", "-v", Volume + ":/var/lib/postgresql/data", "postgres:16",
                }));
                ReviewAppVault.Pass(host.Error, created.Error);
                if (created.ExitCode != 0) throw new ReviewEngineFailure("docker run for Postgres failed.");
            }
            else if (!string.Equals((state.Output ?? "").Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                ReviewAppVault.Pass(host.Error, seams.Run(new ReviewToolCall("docker", new[] { "start", Container })).Error);
            }
            var deadline = host.UtcNow + ReadyWithin;
            while (seams.Run(new ReviewToolCall("docker", new[] { "exec", Container, "pg_isready", "-U", "antiphon", "-d", "antiphon" })).ExitCode != 0)
            {
                if (host.UtcNow > deadline) throw new ReviewEngineFailure("Postgres did not become ready in 60 s.");
                host.Sleep(TimeSpan.FromSeconds(1));
            }

            // The repo mirror reviewer worktrees are cut from.
            var mirrorGit = files.Path.Combine(options.MirrorPath, ".git");
            if (!files.File.Exists(mirrorGit) && !files.Directory.Exists(mirrorGit))
            {
                var clone = seams.Run(new ReviewToolCall("git", new[] { "clone", "--quiet", "https://github.com/" + options.Repo + ".git", options.MirrorPath }));
                ReviewAppVault.Pass(host.Out, clone.Output);
                ReviewAppVault.Pass(host.Error, clone.Error);
                if (clone.ExitCode != 0) throw new ReviewEngineFailure("git clone into " + options.MirrorPath + " failed.");
            }
            else
            {
                var fetch = seams.Run(new ReviewToolCall("git", new[] { "-C", options.MirrorPath, "fetch", "--quiet", "--prune", "origin" }));
                ReviewAppVault.Pass(host.Out, fetch.Output);
                ReviewAppVault.Pass(host.Error, fetch.Error);
            }

            // The app and the review settings, from Bitwarden into the server's environment only.
            var item = ReviewAppVault.Read(options.VaultItem, ReviewAppVault.Locked, options.Home, host, files, seams);
            var work = files.Path.Combine(options.TempRoot, "antiphon-review-standalone");
            files.Directory.CreateDirectory(work);
            var settings = ServerEnvironment(options, item, work, files);

            var serverLog = files.Path.Combine(logDir, "server.log");
            var server = seams.Start(new ReviewDetachedStart("dotnet", new[] { "run", "--project", files.Path.Combine(root, "server"), "--no-launch-profile" }, root,
                serverLog, files.Path.Combine(logDir, "server.err.log"), Variables(settings)));

            int? client = null;
            if (!options.NoClient)
            {
                // The client inherits the settings without the key, and learns the server's address and its own port.
                var environment = Variables(settings);
                environment[KeyVariable] = null;
                environment["services__server__http__0"] = "http://127.0.0.1:" + Number(options.Port);
                environment["VITE_PORT"] = Number(options.ClientPort);
                client = seams.Start(new ReviewDetachedStart("cmd.exe", new[] { "/c", "npx", "vite", "--host", "127.0.0.1" }, files.Path.Combine(root, "client"),
                    files.Path.Combine(logDir, "client.log"), files.Path.Combine(logDir, "client.err.log"), environment));
            }

            files.File.WriteAllText(pidFile, PidsJson(server, client), Encoding.ASCII);
            host.Out.WriteLine("Server pid " + Number(server) + " on http://127.0.0.1:" + Number(options.Port) + " (log " + serverLog + ").");
            if (client != null) host.Out.WriteLine("Client pid " + Number(client.Value) + " on http://localhost:" + Number(options.ClientPort) + ".");
            host.Out.WriteLine("Wait for GET /health, then open /reviews. Stop with --stop.");
            return 0;
        }

        // Stop-Standalone: each recorded pid whose command line is still ours, then the record goes.
        private static int StopStandalone(string pidFile, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            if (!files.File.Exists(pidFile) && !files.Directory.Exists(pidFile))
            {
                host.Out.WriteLine("Nothing recorded as running.");
                return 0;
            }
            var text = files.File.ReadAllText(pidFile);
            var pids = new List<KeyValuePair<string, int>>();
            try
            {
                using var document = JsonDocument.Parse(text);
                foreach (var name in new[] { "server", "client" })
                    pids.Add(new KeyValuePair<string, int>(name, Pid(document.RootElement, name)));
            }
            catch (JsonException e)
            {
                throw new ReviewEngineFailure(pidFile + " is not JSON: " + e.Message);
            }
            foreach (var (name, id) in pids)
            {
                if (id <= 0) continue;
                var line = seams.CommandLine(id);
                if (line == null || !Ours.IsMatch(line)) continue;
                seams.StopTree(id);
                host.Out.WriteLine("Stopped " + name + " (" + Number(id) + ").");
            }
            files.File.Delete(pidFile);
            return 0;
        }

        // $pids.<name>: a number (or a number's text); 0 when absent, null or anything else.
        private static int Pid(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
            return 0;
        }

        /// <summary>The pids record: {"server": &lt;pid&gt;, "client": &lt;pid&gt; or null}, two-space indented.</summary>
        public static string PidsJson(int server, int? client)
        {
            var newLine = Environment.NewLine;
            return "{" + newLine + "  \"server\": " + Number(server) + "," + newLine + "  \"client\": " + (client == null ? "null" : Number(client.Value)) + newLine + "}" + newLine;
        }

        private static Dictionary<string, string> Variables(IEnumerable<KeyValuePair<string, string>> settings)
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in settings) variables[pair.Key] = pair.Value;
            return variables;
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The review-github-setup command (review-github-setup.ps1's body).</summary>
    public static class ReviewGitHubSetup
    {
        /// <summary>The setting that holds the key: redacted in a dry run, never printed.</summary>
        public const string KeySetting = "Review:Secrets:GitHubAppPrivateKey";

        private static readonly Regex Pem = new Regex("BEGIN (RSA )?PRIVATE KEY", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly JsonWriterOptions Compact = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, Indented = false };

        /// <summary>Writes the settings into user-secrets, or prints them with DryRun: 0 done; 1 after one stderr line.</summary>
        public static int Run(ReviewGitHubSetupOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return ReviewAppVault.Guard(host, () => Body(options, host, files, seams));
        }

        /// <summary>The user-secrets settings in the script's order; the merge gate's row is last and only without NoAutoMerge.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Settings(ReviewGitHubSetupOptions options, string clientId, long installationId, string pem)
        {
            var repo = options.Repo;
            var settings = new List<KeyValuePair<string, string>>
            {
                Pair("Review:CodeHosts:Hosts:github:Kind", "GitHub"),
                Pair("Review:CodeHosts:Hosts:github:App:AppId", clientId),
                Pair("Review:CodeHosts:Hosts:github:App:InstallationId", installationId.ToString(CultureInfo.InvariantCulture)),
                Pair("Review:CodeHosts:Hosts:github:App:PrivateKeyConfigKey", KeySetting),
                Pair(KeySetting, pem),
                Pair("Review:PublicBaseUrl", options.PublicBaseUrl),
                Pair("Review:DefaultViewerLogin", options.ViewerLogin),
                Pair("Review:Agents:Repos:0:Repo", repo),
                Pair("Review:Agents:Repos:0:Project", options.Project),
                // Reviewers run on this machine, where the project's checkout fetches refs/pull/N/head.
                Pair("Review:Agents:Runner", "local"),
                // Verify on a Haiku seat (a delegate task). If it cannot answer, verify continues unverified (onFailure: continue), so a
                // large diff no longer fails the run.
                Pair("Review:Completions:Chains:completions-cheap:0:Provider", "haiku-delegate"),
                Pair("Review:Completions:Chains:completions-cheap:0:Transport", "Seat"),
                // No public webhook here: poll the repo for new PRs, pushes and /antiphon commands.
                Pair("Review:Polling:Enabled", "true"),
                Pair("Review:Polling:IntervalSeconds", "120"),
                Pair("Review:Polling:Repos:0:Repo", repo),
                // A one-person repo: the author may decide their own gates.
                Pair("Review:Policy:AuthorMayDecideRepos:0", repo),
            };
            // The merge gate is off for the repo: this instance merges its approved PRs itself, by rebase (design 4.12).
            if (!options.NoAutoMerge) settings.Add(Pair("Review:Policy:GatesOff:merge-gate:0", repo));
            return settings;
        }

        private static int Body(ReviewGitHubSetupOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            var item = ReviewAppVault.Read(options.VaultItem, ReviewAppVault.Locked + " Unlock it first (bitwarden skill).", options.Home, host, files, seams);
            var clientId = item.Field("Client ID");
            var installation = item.Field("Installation ID");
            if (!long.TryParse(installation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var installationId))
                throw new ReviewEngineFailure("Bitwarden item '" + options.VaultItem + "' field 'Installation ID' is not a number.");
            var pem = item.Notes;
            if (!Pem.IsMatch(pem)) throw new ReviewEngineFailure("Bitwarden item '" + options.VaultItem + "' notes do not hold a PEM private key.");
            var settings = Settings(options, clientId, installationId, pem);

            if (options.DryRun)
            {
                foreach (var pair in settings)
                    host.Out.WriteLine(pair.Key + " = " + (pair.Key == KeySetting ? "<PEM, " + pem.Length.ToString(CultureInfo.InvariantCulture) + " chars>" : pair.Value));
                host.Out.WriteLine("Dry run: nothing written.");
                return 0;
            }

            // The key goes in on stdin only; set merges the keys it is given, so other user-secrets stay.
            var written = seams.Run(new ReviewToolCall("dotnet", new[] { "user-secrets", "set", "--id", options.SecretsId }, null, Json(settings) + "\n"));
            ReviewAppVault.Pass(host.Error, written.Error);
            if (written.ExitCode != 0)
                throw new ReviewEngineFailure("dotnet user-secrets set failed (exit " + written.ExitCode.ToString(CultureInfo.InvariantCulture) + ").");

            // Never `user-secrets list` here: it prints values, the key among them.
            host.Out.WriteLine("Set " + settings.Count.ToString(CultureInfo.InvariantCulture) + " review settings in user-secrets '" + options.SecretsId + "': "
                + string.Join(", ", settings.Select(pair => pair.Key)));
            host.Out.WriteLine("Restart the AppHost from the main checkout to load them.");
            return 0;
        }

        /// <summary>The settings as one compact JSON object (ConvertTo-Json -Compress of the ordered table).</summary>
        public static string Json(IEnumerable<KeyValuePair<string, string>> settings)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, Compact))
            {
                writer.WriteStartObject();
                foreach (var pair in settings) writer.WriteString(pair.Key, pair.Value);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static KeyValuePair<string, string> Pair(string key, string value) => new KeyValuePair<string, string>(key, value);
    }

    /// <summary>The review-live-github command (review-live-github.ps1's body).</summary>
    public static class ReviewLiveGitHub
    {
        /// <summary>The build slot's label.</summary>
        public const string Label = "review-live-github";

        /// <summary>The test class run (ReviewLiveGitHubTests).</summary>
        public const string TestClass = "ReviewLiveGitHubTests";

        /// <summary>The app's client id (ReviewLiveGitHubTests.AppIdVariable).</summary>
        public const string AppIdVariable = "ANTIPHON_LIVE_GITHUB_APP_ID";

        /// <summary>The app's installation id (ReviewLiveGitHubTests.InstallationVariable).</summary>
        public const string InstallationVariable = "ANTIPHON_LIVE_GITHUB_INSTALLATION_ID";

        /// <summary>The app's PEM key (ReviewLiveGitHubTests.KeyVariable).</summary>
        public const string KeyVariable = "ANTIPHON_LIVE_GITHUB_APP_KEY";

        /// <summary>The PR's repository (ReviewLiveGitHubTests.RepoVariable).</summary>
        public const string RepoVariable = "ANTIPHON_LIVE_GITHUB_REPO";

        /// <summary>The PR's number (ReviewLiveGitHubTests.PrVariable).</summary>
        public const string PrVariable = "ANTIPHON_LIVE_GITHUB_PR";

        /// <summary>
        /// Checks the PR's label, reads the app and runs the test class under the build slot (see the file header): the run's exit
        /// code; 1 after one stderr line when the PR cannot be read or is not labelled, or the vault refuses.
        /// </summary>
        public static int Run(ReviewLiveGitHubOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (seams == null) throw new ArgumentNullException(nameof(seams));
            return ReviewAppVault.Guard(host, () => Body(options, host, files, seams));
        }

        /// <summary>The test run: dotnet run --project tests/Antiphon.Tests [--no-build] -- --treenode-filter /*/*/ReviewLiveGitHubTests/*.</summary>
        public static IReadOnlyList<string> Command(bool noBuild)
        {
            var command = new List<string> { "dotnet", "run", "--project", "tests/Antiphon.Tests" };
            if (noBuild) command.Add("--no-build");
            command.AddRange(new[] { "--", "--treenode-filter", "/*/*/" + TestClass + "/*" });
            return command;
        }

        private static int Body(ReviewLiveGitHubOptions options, NightlyHost host, IFileSystem files, IReviewEngineSeams seams)
        {
            var pr = options.Pr.ToString(CultureInfo.InvariantCulture);
            var view = seams.Run(new ReviewToolCall("gh", new[] { "pr", "view", pr, "--repo", options.Repo, "--json", "labels", "--jq", ".labels[].name" }));
            ReviewAppVault.Pass(host.Error, view.Error);
            if (view.ExitCode != 0) throw new ReviewEngineFailure("Could not read PR #" + pr + " on " + options.Repo + " with gh.");
            // -contains: a label equal to the required one, case-insensitively.
            if (!ReviewAppVault.Lines(view.Output).Contains(options.RequiredLabel, StringComparer.OrdinalIgnoreCase))
                throw new ReviewEngineFailure("PR #" + pr + " is not labelled " + options.RequiredLabel + "; the live check only posts on a scratch PR.");

            var item = ReviewAppVault.Read(options.VaultItem, ReviewAppVault.Locked, options.Home, host, files, seams);
            // The test run's environment only: the key is never in this process's environment, printed or written to a file.
            var environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AppIdVariable] = item.Field("Client ID"),
                [InstallationVariable] = item.Field("Installation ID"),
                [KeyVariable] = item.Notes,
                [RepoVariable] = options.Repo,
                [PrVariable] = pr,
            };
            return seams.BuildSlot(new BuildSlotOptions
            {
                Label = Label,
                Command = Command(options.NoBuild),
                WorkingDirectory = options.RepoRoot,
                Environment = environment,
            });
        }
    }
}
