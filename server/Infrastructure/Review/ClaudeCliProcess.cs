using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Infrastructure.Review;

/// <summary>
/// <see cref="IClaudeCli"/> over a real <c>claude</c> process (review engine design 7.2):
/// <c>claude -p --model &lt;m&gt; --output-format json --tools &lt;t&gt; [--allowedTools &lt;t&gt;] --setting-sources ""
/// --no-session-persistence --strict-mcp-config</c> with the prompt on stdin. The command is
/// <c>Review:Agents:Cli:Command</c> (<c>claude</c>). Never <c>--bare</c>: it disables the subscription
/// sign-in. The Claude Code nesting markers are removed from the child's environment so a server started
/// from a Claude session can still run it. The prompt is never logged and never put in an exception.
/// </summary>
public sealed class ClaudeCliProcess(IOptions<ReviewAgentSettings> settings, ILogger<ClaudeCliProcess> logger) : IClaudeCli
{
    /// <summary>The variables a parent Claude Code session sets that would make the child think it is nested.</summary>
    public static readonly IReadOnlyList<string> NestingMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_ENTRYPOINT",
    ];

    public async Task<ClaudeCliResult> RunAsync(ClaudeCliRequest request, CancellationToken ct)
    {
        var command = string.IsNullOrWhiteSpace(settings.Value.Cli.Command) ? "claude" : settings.Value.Cli.Command.Trim();
        var environment = NestingMarkers.ToDictionary(name => name, _ => (string?)null);
        var started = Stopwatch.GetTimestamp();
        ReviewChildProcess.Outcome outcome;
        try
        {
            outcome = await ReviewChildProcess.RunAsync(command, Arguments(request), request.Prompt, request.WorkingDirectory, request.Timeout,
                environment, ct);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ClaudeCliException($"'{command}' could not be started in {request.WorkingDirectory}: {ex.Message}. " +
                "Set Review:Agents:Cli:Command to the claude executable.");
        }

        if (outcome.TimedOut)
            throw new ClaudeCliException($"claude -p ({request.Model}) did not finish within {Describe(request.Timeout)}; its process tree was killed.",
                timedOut: true);
        var result = Parse(outcome.ExitCode, outcome.StandardOutput, outcome.StandardError);
        logger.LogInformation("claude -p {Model} answered in {Elapsed:0.0}s as {ResolvedModel} ({InputTokens} in, {OutputTokens} out, ${Cost})",
            request.Model, Stopwatch.GetElapsedTime(started).TotalSeconds, result.Model, result.InputTokens, result.OutputTokens, result.CostUsd);
        return result;
    }

    /// <summary>The argument list; an empty tool list is <c>--tools ""</c> (no tools) and sends no <c>--allowedTools</c>.</summary>
    public static IReadOnlyList<string> Arguments(ClaudeCliRequest request)
    {
        var tools = string.Join(",", request.Tools.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        var arguments = new List<string> { "-p", "--model", request.Model, "--output-format", "json", "--tools", tools };
        if (tools.Length > 0)
            arguments.AddRange(["--allowedTools", tools]);
        arguments.AddRange(["--setting-sources", "", "--no-session-persistence", "--strict-mcp-config"]);
        return arguments;
    }

    /// <summary>
    /// Reads the <c>--output-format json</c> result. A non-zero exit, <c>is_error: true</c>, no JSON object
    /// or no <c>result</c> text is a <see cref="ClaudeCliException"/> carrying the CLI's own error text.
    /// </summary>
    public static ClaudeCliResult Parse(int exitCode, string stdout, string stderr)
    {
        var root = ReadResult(stdout);
        if (root is null)
        {
            var said = Excerpt(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            throw new ClaudeCliException(exitCode == 0
                ? $"claude -p printed no JSON result: {said}"
                : $"claude -p exited with code {exitCode} and no JSON result: {said}");
        }

        var text = root["result"] is JsonValue r && r.TryGetValue<string>(out var t) ? t : null;
        var isError = root["is_error"] is JsonValue e && e.TryGetValue<bool>(out var flag) && flag;
        if (isError || exitCode != 0)
        {
            var details = new List<string>();
            if (exitCode != 0)
                details.Add($"exit {exitCode}");
            if (Text(root["subtype"]) is { } subtype)
                details.Add(subtype);
            if (root["api_error_status"] is JsonValue status && status.ToString() is { Length: > 0 } code && code != "null")
                details.Add($"API {code}");
            var why = isError && !string.IsNullOrWhiteSpace(text) ? text : string.IsNullOrWhiteSpace(stderr) ? text : stderr;
            throw new ClaudeCliException($"claude -p failed ({string.Join(", ", details)}): {Excerpt(why)}");
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new ClaudeCliException("claude -p answered with no result text.");

        var usage = root["usage"] as JsonObject;
        var input = Long(usage, "input_tokens") + Long(usage, "cache_creation_input_tokens") + Long(usage, "cache_read_input_tokens");
        var output = Long(usage, "output_tokens");
        string? model = null;
        if (root["modelUsage"] is JsonObject models && models.Count > 0)
        {
            var main = models.Where(m => m.Value is JsonObject)
                .OrderByDescending(m => Decimal(m.Value!["costUSD"]))
                .ThenByDescending(m => Long(m.Value as JsonObject, "outputTokens"))
                .FirstOrDefault();
            model = main.Key;
            if (usage is null)
            {
                foreach (var (_, node) in models)
                {
                    var entry = node as JsonObject;
                    input += Long(entry, "inputTokens") + Long(entry, "cacheReadInputTokens") + Long(entry, "cacheCreationInputTokens");
                    output += Long(entry, "outputTokens");
                }
            }
        }

        return new ClaudeCliResult(text, model ?? "unknown", input, output, Decimal(root["total_cost_usd"]));
    }

    private static JsonObject? ReadResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;
        if (TryObject(stdout.Trim()) is { } whole)
            return whole;
        // A warning printed ahead of the result: take the last line that is a JSON object.
        foreach (var line in stdout.Split('\n').Select(l => l.Trim()).Reverse())
        {
            if (line.StartsWith('{') && TryObject(line) is { } found)
                return found;
        }

        return null;
    }

    private static JsonObject? TryObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) is JsonObject root && (root.ContainsKey("result") || root.ContainsKey("is_error")) ? root : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long Long(JsonObject? node, string name) =>
        node?[name] is JsonValue value && value.TryGetValue<long>(out var n) ? n
        : node?[name] is JsonValue d && d.TryGetValue<double>(out var x) ? (long)x
        : 0;

    private static decimal Decimal(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var d) ? d
        : node is JsonValue v && v.TryGetValue<double>(out var x) ? (decimal)x
        : 0m;

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) && s.Length > 0 ? s : null;

    public static string Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "(no output)";
        var trimmed = text.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 500 ? trimmed : trimmed[..500] + "...";
    }

    private static string Describe(TimeSpan span) => span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0.#} minutes" : $"{span.TotalSeconds:0}s";
}

/// <summary>
/// <see cref="IReviewCheckout"/> over the <c>git</c> CLI: <c>git -C &lt;repo&gt; fetch --no-tags origin
/// +refs/pull/&lt;n&gt;/head:refs/antiphon/review/pull/&lt;n&gt;</c> when the head is missing, then
/// <c>git -C &lt;repo&gt; worktree add --detach &lt;dir&gt; &lt;sha&gt;</c>; afterwards
/// <c>worktree remove --force</c> and <c>worktree prune</c>. Calls on one repository run one at a time,
/// so two reviewer groups never fetch the same ref at once. A GitHub remote's fetch carries the code host's
/// token as an <c>http.&lt;web root&gt;.extraheader</c> given to git through <c>GIT_CONFIG_COUNT</c> /
/// <c>GIT_CONFIG_KEY_0</c> / <c>GIT_CONFIG_VALUE_0</c> in its environment only, scoped to that host; and a
/// missing mirror (<c>Review:Agents:MirrorRoot</c>) is first created with <c>git init --bare</c> and an origin
/// of <c>{web root}/{owner}/{name}.git</c>.
/// </summary>
public sealed class GitReviewCheckout(ILogger<GitReviewCheckout> logger, IReviewGitCredentials? credentials = null) : IReviewCheckout
{
    private static readonly TimeSpan LocalTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan AddTimeout = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyDictionary<string, string?> GitEnvironment = new Dictionary<string, string?>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);

    public async Task AddAsync(string repoPath, ReviewCheckoutRemote? remote, int? prNumber, string headSha, string directory, CancellationToken ct)
    {
        var gate = Gate(repoPath);
        await gate.WaitAsync(ct);
        try
        {
            ReviewGitCredential? credential = null;
            if (remote is { CreateMirror: true } && !Directory.Exists(repoPath))
            {
                credential = await MirrorCredentialAsync(remote.Repo, ct);
                await CreateMirrorAsync(repoPath, new Uri(credential.WebRoot, remote.Repo.Trim() + ".git"), ct);
            }

            if (!await ResolvesAsync(repoPath, headSha, ct))
            {
                if (prNumber is not { } number || number <= 0)
                    throw new ReviewAgentRunnerException($"The reviewed head {headSha} is not in {repoPath}, and there is no PR number to fetch it by.");
                if (remote is not null)
                    credential ??= await CredentialAsync(remote.Repo, ct);
                var target = $"refs/antiphon/review/pull/{number}";
                var fetch = await GitAsync(repoPath, ["fetch", "--no-tags", "origin", $"+refs/pull/{number}/head:{target}"], FetchTimeout, ct,
                    FetchEnvironment(credential));
                if (!fetch.Succeeded)
                    throw new ReviewAgentRunnerException($"Fetching refs/pull/{number}/head from origin into {repoPath} failed: {fetch.Diagnostic}");
                if (!await ResolvesAsync(repoPath, headSha, ct))
                    throw new ReviewAgentRunnerException($"origin's refs/pull/{number}/head does not contain {headSha} (the PR moved; Refresh the run).");
                logger.LogInformation("Fetched PR {PrNumber} head {HeadSha} into {RepoPath} for a one-shot reviewer", number, headSha, repoPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(directory))!);
            var add = await GitAsync(repoPath, ["worktree", "add", "--detach", directory, headSha], AddTimeout, ct);
            if (!add.Succeeded)
                throw new ReviewAgentRunnerException($"git worktree add at {headSha} in {repoPath} failed: {add.Diagnostic}");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(string repoPath, string directory, CancellationToken ct)
    {
        var gate = Gate(repoPath);
        try
        {
            await gate.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var remove = await GitAsync(repoPath, ["worktree", "remove", "--force", directory], LocalTimeout, ct);
            if (!remove.Succeeded)
            {
                logger.LogWarning("git worktree remove {Directory} in {RepoPath} failed ({Diagnostic}); deleting the folder", directory, repoPath,
                    remove.Diagnostic);
                ReviewChildProcess.TryDeleteDirectory(directory);
            }

            var prune = await GitAsync(repoPath, ["worktree", "prune"], LocalTimeout, ct);
            if (!prune.Succeeded)
                logger.LogWarning("git worktree prune in {RepoPath} failed: {Diagnostic}", repoPath, prune.Diagnostic);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Removing the one-shot reviewer worktree {Directory} in {RepoPath} failed", directory, repoPath);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim Gate(string repoPath) => _gates.GetOrAdd(Path.GetFullPath(repoPath).TrimEnd('\\', '/'), _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// The host's credential for a fetch into an existing clone, or none when it cannot be had: the fetch then
    /// runs without a token, as it did before (a public repo, or a clone with its own credential helper).
    /// </summary>
    private async Task<ReviewGitCredential?> CredentialAsync(string repo, CancellationToken ct)
    {
        if (credentials is null)
            return null;
        try
        {
            return await credentials.GetAsync(repo, ct);
        }
        catch (CodeHostException ex)
        {
            // The message is ours or GitHub's error text; neither carries a token.
            logger.LogWarning("No git credential for {Repo} ({Reason}); fetching without one", repo, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// The credential a new mirror needs for its origin's web root: a host that cannot give one fails the
    /// reviewer with the reason rather than leave a mirror pointing at the wrong host.
    /// </summary>
    private async Task<ReviewGitCredential> MirrorCredentialAsync(string repo, CancellationToken ct)
    {
        if (credentials is null)
            return new ReviewGitCredential(GitHubWebRoot.Default, null);
        try
        {
            return await credentials.GetAsync(repo, ct);
        }
        catch (CodeHostException ex)
        {
            throw new ReviewAgentRunnerException($"No mirror of {repo} could be made: {ex.Message}");
        }
    }

    /// <summary><c>git init --bare</c> plus the origin; a half-made mirror is deleted so the next review tries again.</summary>
    private async Task CreateMirrorAsync(string repoPath, Uri origin, CancellationToken ct)
    {
        var full = Path.GetFullPath(repoPath);
        var parent = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(parent);
        var init = await GitAsync(parent, ["init", "--bare", "--quiet", full], LocalTimeout, ct);
        var remote = init.Succeeded ? await GitAsync(full, ["remote", "add", "origin", origin.AbsoluteUri], LocalTimeout, ct) : init;
        if (!remote.Succeeded)
        {
            ReviewChildProcess.TryDeleteDirectory(full);
            throw new ReviewAgentRunnerException($"Creating the mirror {full} of {origin.AbsoluteUri} failed: {remote.Diagnostic}");
        }

        logger.LogInformation("Created the reviewer mirror {RepoPath} of {Origin}", full, origin.AbsoluteUri);
    }

    /// <summary>
    /// The fetch's environment: with a token, one <c>http.&lt;web root&gt;.extraheader</c> through git's
    /// <c>GIT_CONFIG_*</c> variables, so the token is never an argument and only goes to that host.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> FetchEnvironment(ReviewGitCredential? credential)
    {
        if (credential is not { Token: { Length: > 0 } token })
            return GitEnvironment;
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token));
        return new Dictionary<string, string?>(GitEnvironment)
        {
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = $"http.{credential.WebRoot.AbsoluteUri}.extraheader",
            ["GIT_CONFIG_VALUE_0"] = "AUTHORIZATION: basic " + basic,
        };
    }

    private async Task<bool> ResolvesAsync(string repoPath, string sha, CancellationToken ct) =>
        (await GitAsync(repoPath, ["rev-parse", "--verify", "--quiet", sha + "^{commit}"], LocalTimeout, ct)).Succeeded;

    private static async Task<(bool Succeeded, string Diagnostic)> GitAsync(string repoPath, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken ct, IReadOnlyDictionary<string, string?>? environment = null)
    {
        ReviewChildProcess.Outcome outcome;
        try
        {
            outcome = await ReviewChildProcess.RunAsync("git", ["-C", repoPath, .. arguments], null, Path.GetTempPath(), timeout,
                environment ?? GitEnvironment, ct);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            throw new ReviewAgentRunnerException($"git could not be started: {ex.Message}");
        }

        if (outcome.TimedOut)
            return (false, $"git {arguments[0]} did not finish within {timeout.TotalMinutes:0} minutes");
        return (outcome.ExitCode == 0, outcome.ExitCode == 0 ? "" : $"exit {outcome.ExitCode}: " +
            ClaudeCliProcess.Excerpt(string.IsNullOrWhiteSpace(outcome.StandardError) ? outcome.StandardOutput : outcome.StandardError));
    }
}

/// <summary>
/// One child process for the review engine's one-shot transports: arguments as a list (no shell), optional
/// stdin written as UTF-8 and closed, stdout and stderr captured, and the whole process tree killed on the
/// timeout or on cancellation.
/// </summary>
internal static class ReviewChildProcess
{
    public sealed record Outcome(int ExitCode, string StandardOutput, string StandardError, bool TimedOut);

    private static readonly TimeSpan DrainAfterKill = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Runs <paramref name="fileName"/>. <paramref name="environment"/> sets variables, and a null value removes one.
    /// Returns <see cref="Outcome.TimedOut"/> after killing the tree at the timeout; throws
    /// <see cref="OperationCanceledException"/> after killing it when <paramref name="ct"/> is cancelled.
    /// </summary>
    public static async Task<Outcome> RunAsync(string fileName, IReadOnlyList<string> arguments, string? stdin, string workingDirectory,
        TimeSpan timeout, IReadOnlyDictionary<string, string?>? environment, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };
        if (stdin is not null)
            info.StandardInputEncoding = new UTF8Encoding(false);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                if (value is null)
                    info.Environment.Remove(name);
                else
                    info.Environment[name] = value;
            }
        }

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMilliseconds(1));
        try
        {
            if (stdin is not null)
            {
                try
                {
                    await process.StandardInput.WriteAsync(stdin.AsMemory(), limit.Token);
                    await process.StandardInput.FlushAsync(limit.Token);
                }
                catch (IOException)
                {
                    // The child exited (or closed stdin) before reading it all; its exit code and stderr say why.
                }
                finally
                {
                    try
                    {
                        process.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                    }
                }
            }

            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            var partialOut = await DrainAsync(stdout);
            var partialErr = await DrainAsync(stderr);
            ct.ThrowIfCancellationRequested();
            return new Outcome(-1, partialOut, partialErr, TimedOut: true);
        }

        return new Outcome(process.ExitCode, await stdout, await stderr, TimedOut: false);
    }

    public static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.WaitForExit(DrainAfterKill);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static async Task<string> DrainAsync(Task<string> read)
    {
        var done = await Task.WhenAny(read, Task.Delay(DrainAfterKill));
        return done == read && read.IsCompletedSuccessfully ? read.Result : "";
    }
}
