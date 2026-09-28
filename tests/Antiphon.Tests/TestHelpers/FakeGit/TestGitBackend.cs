using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Antiphon.Tests.TestHelpers.FakeGit;

/// <summary>Freezes the opt-in choice once for a test host.</summary>
public sealed class TestGitBackendHost
{
    public bool UseRealGit { get; }
    private readonly IReadOnlyDictionary<string, string>? _ambient;

    public TestGitBackendHost(Func<string?> readSwitch,
        IReadOnlyDictionary<string, string>? ambient = null)
    {
        _ambient = ambient;
        var value = readSwitch();
        UseRealGit = value switch
        {
            null or "0" => false,
            "1" => true,
            _ => throw new ArgumentException("ANTIPHON_TEST_REAL_GIT must be 0, 1 or unset.")
        };
    }

    public TestGitBackend Create() => new(UseRealGit, _ambient);
}

/// <summary>One repository, one command model, one filesystem and one launch observer.</summary>
public sealed class TestGitBackend : IAsyncDisposable
{
    private sealed class RecordingExecutor(IGitCommandExecutor inner, List<GitCommandTrace> trace,
        Func<string> role)
        : IGitCommandExecutor
    {
        public async Task<GitCommandResult> ExecuteAsync(string workingDirectory,
            IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            var result = await inner.ExecuteAsync(workingDirectory, arguments, timeout, ct);
            trace.Add(new GitCommandTrace(workingDirectory, arguments.ToArray(), result, role()));
            return result;
        }
    }
    private sealed class ServiceFaultExecutor(IGitCommandExecutor inner,
        AsyncLocal<string?> role, List<GitCommandTrace> trace) : IGitCommandExecutor
    {
        private string[]? _vector;
        private GitCommandResult? _result;
        public void Inject(string[] vector, GitCommandResult result)
        {
            _vector = vector;
            _result = result;
        }
        public async Task<GitCommandResult> ExecuteAsync(string workingDirectory,
            IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var previous = role.Value;
            role.Value = "service";
            try
            {
                if (_vector is not null && arguments.SequenceEqual(_vector, StringComparer.Ordinal))
                {
                    var result = _result!;
                    _vector = null;
                    _result = null;
                    trace.Add(new GitCommandTrace(workingDirectory, arguments.ToArray(), result,
                        "service-fault"));
                    return result;
                }
                return await inner.ExecuteAsync(workingDirectory, arguments, timeout, ct);
            }
            finally { role.Value = previous; }
        }
    }
    private readonly List<string> _failures = [];
    private readonly List<GitCommandTrace> _trace = [];
    private readonly AsyncLocal<string?> _role = new();
    private readonly string? _home;
    private readonly IGitCommandExecutor _executor;
    private readonly ServiceFaultExecutor _serviceExecutor;
    public bool IsReal { get; }
    public string Storage => IsReal ? "physical" : "mock";
    public string RepoPath { get; }
    public string? HomePath => _home;
    public IFileSystem Files { get; }
    public GitService Service { get; }
    public FakeGit? Model { get; }
    public int GitLaunches { get; private set; }
    public IReadOnlyList<string> Failures => _failures.Concat(Model?.Failures ?? []).ToArray();
    public IReadOnlyList<GitCommandTrace> Trace => _trace;

    internal TestGitBackend(bool useReal, IReadOnlyDictionary<string, string>? ambient = null)
    {
        IsReal = useReal;
        Files = useReal ? new System.IO.Abstractions.FileSystem() : new MockFileSystem();
        RepoPath = Path.Combine(Path.GetTempPath(), "antiphon-fakegit-" + Guid.NewGuid().ToString("N"));
        Files.Directory.CreateDirectory(RepoPath);
        if (useReal)
        {
            _home = Path.Combine(Path.GetTempPath(), "antiphon-fakegit-home-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_home);
            var env = ambient?.ToDictionary(pair => pair.Key, pair => pair.Value,
                StringComparer.Ordinal) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            env["HOME"] = _home;
            env["GIT_CONFIG_GLOBAL"] = Path.Combine(_home, "config");
            env["GIT_CONFIG_NOSYSTEM"] = "1";
            env["GIT_TERMINAL_PROMPT"] = "0";
            env["GIT_ASKPASS"] = OperatingSystem.IsWindows() ? "NUL" : "/bin/false";
            env["GIT_AUTHOR_NAME"] = "Antiphon Test";
            env["GIT_AUTHOR_EMAIL"] = "test@antiphon.dev";
            env["GIT_COMMITTER_NAME"] = "Antiphon Test";
            env["GIT_COMMITTER_EMAIL"] = "test@antiphon.dev";
            _executor = new RecordingExecutor(new CliGitCommandExecutor(env, () => GitLaunches++),
                _trace, () => _role.Value ?? "observer");
        }
        else
        {
            Model = new FakeGit(Files);
            _executor = new RecordingExecutor(Model, _trace, () => _role.Value ?? "observer");
        }
        _serviceExecutor = new ServiceFaultExecutor(_executor, _role, _trace);
        Service = new GitService(NullLogger<GitService>.Instance,
            executor: _serviceExecutor, fileSystem: Files);
    }

    public async Task<GitCommandResult> RunAsync(params string[] args)
    {
        return await _executor.ExecuteAsync(RepoPath, args, TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    public async Task<string> RequiredAsync(params string[] args)
    {
        var result = await RunAsync(args);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', args)}: {result.Stderr}");
        return result.Stdout;
    }

    public async Task InitializeAsync()
    {
        var previous = _role.Value;
        _role.Value = "setup";
        try
        {
            await RequiredAsync("init", "-b", "master");
            await RequiredAsync("config", "user.email", "test@antiphon.dev");
            await RequiredAsync("config", "user.name", "Antiphon Test");
            await Files.File.WriteAllTextAsync(Path.Combine(RepoPath, "README.md"), "# Test Repo");
            await RequiredAsync("add", ".");
            await RequiredAsync("commit", "-m", "Initial commit");
        }
        finally { _role.Value = previous; }
    }

    public IGitCommandExecutor ConstructRealExecutor()
    {
        if (!IsReal)
        {
            _failures.Add("real executor construction escaped fake fixture");
            throw new InvalidOperationException(_failures[^1]);
        }
        return _executor;
    }

    public async Task<GitCommandResult> ExecuteRealAsync(params string[] args)
    {
        if (!IsReal)
        {
            _failures.Add("real executor execution escaped fake fixture");
            throw new InvalidOperationException(_failures[^1]);
        }
        return await _executor.ExecuteAsync(RepoPath, args, TimeSpan.FromSeconds(30), CancellationToken.None);
    }

    public void InjectServiceResult(string[] vector, GitCommandResult result) =>
        _serviceExecutor.Inject(vector, result);

    public void AssertHealthy()
    {
        if (Failures.Count != 0) throw new InvalidOperationException(string.Join("; ", Failures));
        if (!IsReal && GitLaunches != 0) throw new InvalidOperationException("Git launched in fake fixture");
    }

    public async Task SaveReceiptAsync(string method, string[] branches, string[] tags, string[] paths)
    {
        var sample = Environment.GetEnvironmentVariable("ANTIPHON_FAKEGIT_SAMPLE");
        if (string.IsNullOrWhiteSpace(sample)) return;
        var observation = await GitContractObservation.CaptureAsync(this, branches, tags, paths);
        var receipt = new
        {
            checkpoint = sample,
            backend = IsReal ? "real" : "fake",
            storage = Storage,
            @class = "Antiphon.Tests.Infrastructure.GitServiceTests",
            method,
            arguments = Array.Empty<string>(),
            outcome = "passed",
            launchObserverPresent = true,
            observedGitLaunches = GitLaunches,
            fixtureFailureLedger = Failures,
            observations = observation,
            commands = Trace.Select(t => new
            {
                argv = t.Arguments,
                role = t.Role,
                cwd = "<repo>",
                t.Result.ExitCode,
                t.Result.Stdout,
                t.Result.Stderr
            }).ToArray()
        };
        var folder = ReceiptFolder(sample);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, method + ".json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void SavePureReceipt(string method, string value, bool useReal)
    {
        var sample = Environment.GetEnvironmentVariable("ANTIPHON_FAKEGIT_SAMPLE");
        if (string.IsNullOrWhiteSpace(sample)) return;
        var folder = ReceiptFolder(sample);
        Directory.CreateDirectory(folder);
        var receipt = new
        {
            checkpoint = sample,
            backend = useReal ? "real" : "fake",
            storage = useReal ? "physical" : "mock",
            @class = "Antiphon.Tests.Infrastructure.GitServiceTests",
            method,
            arguments = Array.Empty<string>(),
            outcome = "passed",
            launchObserverPresent = true,
            observedGitLaunches = 0,
            fixtureFailureLedger = Array.Empty<string>(),
            observations = new { value },
            commands = Array.Empty<object>()
        };
        File.WriteAllText(Path.Combine(folder, method + ".json"), JsonSerializer.Serialize(receipt));
    }

    private static string ReceiptFolder(string sample)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".git"))
            && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Cannot find repository root for FakeGit receipt.");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var processKey = process.Id + "-" + process.StartTime.ToUniversalTime().ToString("yyyyMMddHHmmssfff");
        return Path.Combine(directory.FullName, ".antiphon", "fakegit-receipts", sample + "-" + processKey);
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
        if (IsReal)
        {
            DeleteTree(RepoPath);
            if (_home is not null) DeleteTree(_home);
        }
        AssertHealthy();
    }

    private static void DeleteTree(string path)
    {
        if (!Directory.Exists(path)) return;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(path, true);
    }
}
