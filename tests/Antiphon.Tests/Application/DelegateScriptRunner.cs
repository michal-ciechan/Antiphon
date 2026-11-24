using System.IO.Abstractions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.Application;

/// <summary>
/// Runs the delegate command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the
/// 2026-11-22 plan) in process, pointed at a test-owned API base URL, as the CLI runs it: the parse, then the run on the
/// production HTTP transport (DelegateCli.Deadline) and a version probe transport with the options' deadline. A usage error
/// answers exit 2 with its message on the error text. The output is stdout then stderr, as the pwsh runner joined them.
///
/// <para>The environment the CLI would read comes only from <c>environment</c> (ANTIPHON_TASK_TOKEN, ANTIPHON_CAPABILITY,
/// ANTIPHON_CAPABILITY_STORE, ANTIPHON_OPERATOR_TOKEN_FILE, ANTIPHON_VERSION_PROBE_TIMEOUT_SEC, X_LLM_PROJECT, X_LLM_KEY): the
/// test process's own variables never leak in, and the capability store and operator token file default to paths that do not
/// exist. git (the checkout sent as workingDirectory, the bind diagnosis) is the real git on the working directory, which
/// defaults to this process's. Shared by every delegate test so all of them drive one caller.</para>
///
/// <para><see cref="RepoRoot"/> is the checkout root; many tests use it to read repository files.</para>
/// </summary>
internal static class DelegateScriptRunner
{
    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        "ANTIPHON_TASK_TOKEN", "ANTIPHON_CAPABILITY", "ANTIPHON_CAPABILITY_STORE", "ANTIPHON_OPERATOR_TOKEN_FILE",
        "ANTIPHON_VERSION_PROBE_TIMEOUT_SEC", "X_LLM_PROJECT", "X_LLM_KEY",
    };

    /// <param name="apiBaseUrl">What the command sees as <c>ANTIPHON_API</c>; a trailing slash is tolerated (it is trimmed).</param>
    /// <param name="args">The command line after <c>delegate</c>: the verb first.</param>
    public static Task<(int ExitCode, string Output)> RunAsync(string apiBaseUrl, params string[] args) =>
        RunAsync(apiBaseUrl, environment: null, args);

    public static Task<(int ExitCode, string Output)> RunAsync(
        string apiBaseUrl,
        IReadOnlyDictionary<string, string?>? environment,
        params string[] args) =>
        RunAsync(apiBaseUrl, environment, workingDirectory: null, args);

    public static Task<(int ExitCode, string Output)> RunAsync(
        string apiBaseUrl,
        IReadOnlyDictionary<string, string?>? environment,
        string? workingDirectory,
        IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (!Known.Contains(name)) throw new ArgumentException("the delegate command does not read " + name, nameof(environment));
            values[name] = value;
        }
        string Read(string name, string fallback) => values.TryGetValue(name, out var value) && value is not null ? value : fallback;
        var llm = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in new[] { "X_LLM_PROJECT", "X_LLM_KEY" })
            if (values.TryGetValue(name, out var value) && value is not null) llm[name] = value;
        var missing = Path.Combine(Path.GetTempPath(), "delegate-runner-unset-" + Guid.NewGuid().ToString("N"));
        var inputs = new DelegateCliEnvironment
        {
            Api = apiBaseUrl,
            TaskToken = Read("ANTIPHON_TASK_TOKEN", ""),
            Capability = Read("ANTIPHON_CAPABILITY", ""),
            CapabilityStore = Read("ANTIPHON_CAPABILITY_STORE", Path.Combine(missing, "capabilities")),
            OperatorTokenFile = Read("ANTIPHON_OPERATOR_TOKEN_FILE", Path.Combine(missing, "operator-token")),
            ProbeTimeoutSeconds = Read("ANTIPHON_VERSION_PROBE_TIMEOUT_SEC", ""),
            LlmEnvironment = llm,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Environment.CurrentDirectory : workingDirectory,
        };
        // The transport blocks on its HTTP call; run it off the caller's context, as the child process ran beside it.
        return Task.Run(() => Run(inputs, args));
    }

    private static (int ExitCode, string Output) Run(DelegateCliEnvironment inputs, IReadOnlyList<string> args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var options = DelegateCliOptions.Parse(args.ToArray(), inputs);
            var host = new NightlyHost
            {
                Clock = TimeProvider.System,
                Git = NightlyNativeGit.Run,
                Launcher = new RecordingLauncher(),
                Processes = new FakeProcessTable(),
                Probe = new FakeHostProbe(),
                Out = output,
                Error = error,
                Trace = TextWriter.Null,
            };
            var exit = DelegateCli.Run(options, host, new FileSystem(), NightlyReport.HttpTransport(timeout: DelegateCli.Deadline),
                NightlyReport.HttpTransport(timeout: options.ProbeTimeout),
                OperatingSystem.IsWindows() ? new Func<byte[], byte[]>(CapabilityCli.UnprotectCurrentUser) : null);
            return (exit, output.ToString() + error.ToString());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: " + e.Message);
            return (2, output.ToString() + error.ToString());
        }
    }

    /// <summary>The checkout root, found by walking up from the test binaries to the solution file.</summary>
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
        }
    }
}
