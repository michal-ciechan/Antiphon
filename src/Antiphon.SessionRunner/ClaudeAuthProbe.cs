using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0628 D-7. Answers "is this provider's CLI signed in inside the runner's own state?" for the
/// <see cref="PhoneHomeOperation.ProviderAuth"/> operation and the launch-time backstop.
/// </summary>
public interface IProviderAuthProbe
{
    Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct);
}

/// <summary>
/// CARD-0628 D-7. Measures Claude Code's sign-in state on the runner.
///
/// <para>Provisioned means either of the two stores the operator may choose (card decision,
/// 2026-09-23): a <c>claude setup-token</c> OAuth token in the runner's own
/// <c>CLAUDE_CODE_OAUTH_TOKEN</c>, or an interactive login's <c>.credentials.json</c> under
/// <see cref="PhoneHomeSettings.ClaudeHome"/>. An Anthropic API key is never a fallback: the probe
/// strips <c>ANTHROPIC_API_KEY</c>/<c>ANTHROPIC_AUTH_TOKEN</c> from the CLI's environment, and a
/// CLI answer whose auth method is an API key counts as signed out.</para>
///
/// <para><c>claude auth status --json</c> still runs (with <c>CLAUDE_CONFIG_DIR</c> pointed at the
/// store) because it is the only source of the auth method and subscription type. Its raw output
/// carries the account's email and organisation, so it is never logged or returned: only
/// <c>loggedIn</c>, <c>authMethod</c> and <c>subscriptionType</c> survive the parse, and a token
/// value is never read beyond "is it non-empty".</para>
///
/// <para>A probe that cannot tell (timeout, missing binary, unparseable output) answers
/// <c>LoggedIn = null</c> with a one-word error class, and callers admit on that: a probe never
/// blocks a launch that would have worked (<c>GrokCredentialStore</c>'s rule).</para>
/// </summary>
public sealed class ClaudeAuthProbe : IProviderAuthProbe
{
    public const string ProviderName = "claude";
    public const string OAuthTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";
    public const string CredentialsFileName = ".credentials.json";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Credential variables the probe's CLI never sees (card decision 1: no API-key fallback).</summary>
    public static readonly IReadOnlyList<string> StrippedVariables = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"];

    private readonly PhoneHomeSettings _settings;
    private readonly ILogger _logger;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Stdout, string Stderr)>> _run;
    private readonly Func<string, string?> _environment;
    private readonly Func<string, bool> _fileExists;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _timeout;

    public ClaudeAuthProbe(
        PhoneHomeSettings settings,
        ILogger<ClaudeAuthProbe>? logger = null,
        Func<ProcessStartInfo, CancellationToken, Task<(int ExitCode, string Stdout, string Stderr)>>? run = null,
        Func<string, string?>? environment = null,
        Func<string, bool>? fileExists = null,
        TimeProvider? clock = null,
        TimeSpan? timeout = null)
    {
        _settings = settings;
        _logger = logger ?? NullLogger<ClaudeAuthProbe>.Instance;
        _run = run ?? RunAsync;
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _fileExists = fileExists ?? File.Exists;
        _clock = clock ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>The POSIX path of the interactive login's credential file inside the store.</summary>
    public string CredentialsPath => _settings.ClaudeHome.TrimEnd('/') + "/" + CredentialsFileName;

    public async Task<RunnerProviderAuthDto> ProbeAsync(string provider, CancellationToken ct)
    {
        if (!string.Equals(provider, ProviderName, StringComparison.OrdinalIgnoreCase))
            throw new PhoneHomeAdmissionException(
                PhoneHomeProblemTypes.UnsupportedTarget, $"Provider '{provider}' has no auth probe on this runner.", 400);

        var hasToken = !string.IsNullOrWhiteSpace(_environment(OAuthTokenVariable));
        var hasCredentialsFile = SafeExists(CredentialsPath);
        var cli = await ProbeCliAsync(ct);
        if (cli.LoggedIn == true && IsApiKeyMethod(cli.AuthMethod))
            cli = cli with { LoggedIn = false, Error = "api_key_refused" };

        CliAnswer answer = hasToken || hasCredentialsFile
            ? cli.LoggedIn == true
                ? cli with { Error = null }
                : new CliAnswer(true, hasToken ? "oauth_token" : "credentials_file", null, null)
            : cli;

        var dto = new RunnerProviderAuthDto(
            ProviderName, answer.LoggedIn, answer.AuthMethod, answer.SubscriptionType, _clock.GetUtcNow(), answer.Error);
        _logger.LogInformation(
            "Claude auth probe: loggedIn={LoggedIn} authMethod={AuthMethod} subscriptionType={SubscriptionType} "
            + "tokenPresent={TokenPresent} credentialsFilePresent={CredentialsFilePresent} error={Error}",
            dto.LoggedIn, dto.AuthMethod, dto.SubscriptionType, hasToken, hasCredentialsFile, dto.Error);
        return dto;
    }

    private async Task<CliAnswer> ProbeCliAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("auth");
        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--json");
        psi.Environment["CLAUDE_CONFIG_DIR"] = _settings.ClaudeHome;
        psi.Environment["DISABLE_AUTOUPDATER"] = "1";
        foreach (var name in StrippedVariables)
            psi.Environment.Remove(name);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        int exitCode;
        string stdout;
        try
        {
            (exitCode, stdout, _) = await _run(psi, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return CliAnswer.Unknown("timeout");
        }
        catch (Win32Exception)
        {
            return CliAnswer.Unknown("missing_binary");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return CliAnswer.Unknown("spawn_failed");
        }

        return Parse(exitCode, stdout);
    }

    /// <summary>
    /// Keeps exactly three fields. Everything else in the JSON (email, orgId, orgName, ...) is
    /// dropped here and never reaches a DTO or a log line.
    /// </summary>
    internal static CliAnswer Parse(int exitCode, string stdout)
    {
        try
        {
            using var doc = JsonDocument.Parse(stdout.Trim());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("loggedIn", out var loggedIn)
                && loggedIn.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return new CliAnswer(loggedIn.GetBoolean(), ReadString(root, "authMethod"), ReadString(root, "subscriptionType"), null);
            }
        }
        catch (JsonException)
        {
        }

        return CliAnswer.Unknown(exitCode == 0 ? "unparseable" : "nonzero_exit");
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool IsApiKeyMethod(string? method)
    {
        if (string.IsNullOrWhiteSpace(method))
            return false;
        var squashed = new string(method.Where(char.IsLetter).ToArray());
        return squashed.Contains("apikey", StringComparison.OrdinalIgnoreCase);
    }

    private bool SafeExists(string path)
    {
        try
        {
            return _fileExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("claude did not start");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    internal sealed record CliAnswer(bool? LoggedIn, string? AuthMethod, string? SubscriptionType, string? Error)
    {
        public static CliAnswer Unknown(string error) => new(null, null, null, error);
    }
}
