using System.Diagnostics;
using System.Xml.Linq;
using Antiphon.Checkpoints;
using Shouldly;

namespace Antiphon.Tests.Checkpoints;

// CARD-1199 S-B1. Isolated fixture Git, seed dotnet, and pwsh children.
// Production HardenGit is unchanged. GIT_ATTR_NOSYSTEM is a fixture restriction.
internal sealed class CheckpointFixtureGitEnvironment : IDisposable
{
    private const string CredentialPrerequisite = "nuget-credential-copy-required";
    private static readonly object Gate = new();
    private static readonly string[] PortableRemovals =
    [
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_OBJECT_DIRECTORY",
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_IMPLICIT_WORK_TREE",
        "GIT_GRAFT_FILE",
        "GIT_INDEX_FILE",
        "GIT_NO_REPLACE_OBJECTS",
        "GIT_REPLACE_REF_BASE",
        "GIT_PREFIX",
        "GIT_SHALLOW_FILE",
        "GIT_COMMON_DIR",
        "GIT_CONFIG",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_COUNT",
        "GIT_NAMESPACE",
        "GIT_CEILING_DIRECTORIES",
    ];

    private static bool _ready;
    private static string[] _queriedRemovals = [];
    private static string _cliHome = "";
    private static string _packages = "";
    private static string _restoreTemplate = "";
    private static int _mappingCount;

    private readonly Dictionary<string, string> _child;

    private CheckpointFixtureGitEnvironment(string scratch, string home, string globalConfig, string systemConfig, string restoreConfig)
    {
        Scratch = scratch;
        Home = home;
        GlobalConfigPath = globalConfig;
        SystemConfigPath = systemConfig;
        RestoreConfigPath = restoreConfig;
        _child = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_GLOBAL"] = globalConfig,
            ["GIT_CONFIG_SYSTEM"] = systemConfig,
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_NO_REPLACE_OBJECTS"] = "1",
            ["GIT_ATTR_NOSYSTEM"] = "1",
            ["HOME"] = home,
            ["USERPROFILE"] = home,
            ["XDG_CONFIG_HOME"] = Path.Combine(scratch, "xdg"),
            ["NUGET_PACKAGES"] = _packages,
            ["DOTNET_CLI_HOME"] = _cliHome,
            ["RestoreConfigFile"] = restoreConfig,
        };
    }

    public string Scratch { get; }

    public string Home { get; }

    public string GlobalConfigPath { get; }

    public string SystemConfigPath { get; }

    public string RestoreConfigPath { get; }

    public int MappingCount => _mappingCount;

    public static CheckpointFixtureGitEnvironment Create(string scratch)
    {
        EnsureReady();
        Directory.CreateDirectory(scratch);
        var home = Path.Combine(scratch, "home");
        var xdg = Path.Combine(scratch, "xdg");
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(xdg);
        var global = Path.Combine(scratch, "global.gitconfig");
        var system = Path.Combine(scratch, "system.gitconfig");
        File.WriteAllBytes(global, []);
        File.WriteAllBytes(system, []);
        var restore = Path.Combine(scratch, "NuGet.Config");
        File.WriteAllText(restore, _restoreTemplate);
        return new CheckpointFixtureGitEnvironment(scratch, home, global, system, restore);
    }

    public string Git(string repository, params string[] args)
    {
        var stdout = RunGit(repository, args);
        if (args.Length > 0 && string.Equals(args[0], "init", StringComparison.Ordinal))
            RunGit(repository, "config", "core.autocrlf", "false");
        return stdout;
    }

    // Direct children can drop inherited Git variables. ProcessDriver can only assign.
    public void Apply(ProcessStartInfo start)
    {
        foreach (var name in RemovalNames())
            start.Environment.Remove(name);
        foreach (var pair in _child)
            start.Environment[pair.Key] = pair.Value;
        if (IsDotnetRestore(start))
            AppendRestoreProperty(start);
    }

    public ProcessStartInfo Command(string fileName, string workingDirectory)
    {
        var start = new ProcessStartInfo(fileName) { WorkingDirectory = workingDirectory };
        Apply(start);
        return start;
    }

    public IConfinedDriver Driver(IConfinedDriver inner) => new IsolatedDriver(inner, this);

    public CheckpointApp.Runtime ToolRuntime(TextWriter output) =>
        new()
        {
            Output = output,
            Driver = Driver(new ProcessDriver()),
        };

    public void Dispose()
    {
    }

    private string RunGit(string repository, params string[] args)
    {
        var start = Command("git", repository);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        var output = CheckpointFixtureProcess.Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stdout + output.Stderr);
        return output.Stdout;
    }

    private void AppendRestoreProperty(ProcessStartInfo start)
    {
        const string prefix = "--property:RestoreConfigFile=";
        foreach (var arg in start.ArgumentList)
        {
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return;
        }

        start.ArgumentList.Add(prefix + RestoreConfigPath);
    }

    private static bool IsDotnetRestore(ProcessStartInfo start)
    {
        var name = Path.GetFileName(start.FileName);
        if (!name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        if (start.ArgumentList.Count == 0)
            return false;
        var verb = start.ArgumentList[0];
        return verb.Equals("build", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("restore", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("test", StringComparison.OrdinalIgnoreCase)
            || verb.Equals("msbuild", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> RemovalNames()
    {
        var names = new HashSet<string>(PortableRemovals, StringComparer.Ordinal);
        foreach (var name in _queriedRemovals)
            names.Add(name);
        foreach (string key in Environment.GetEnvironmentVariables().Keys)
        {
            if (key.StartsWith("GIT_CONFIG_KEY_", StringComparison.Ordinal)
                || key.StartsWith("GIT_CONFIG_VALUE_", StringComparison.Ordinal))
                names.Add(key);
        }

        return names;
    }

    private static void EnsureReady()
    {
        if (_ready)
            return;
        lock (Gate)
        {
            if (_ready)
                return;
            _queriedRemovals = QueryLocalEnvVars();
            _packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ?? "";
            if (string.IsNullOrWhiteSpace(_packages))
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _packages = Path.Combine(profile, ".nuget", "packages");
            }

            _cliHome = Path.Combine(Path.GetTempPath(), "c1199-dotnet-cli-home");
            Directory.CreateDirectory(_cliHome);
            var marker = Path.Combine(_cliHome, ".c1199-ready");
            if (!File.Exists(marker))
            {
                var info = new ProcessStartInfo("dotnet");
                info.ArgumentList.Add("--info");
                info.Environment["DOTNET_CLI_HOME"] = _cliHome;
                info.Environment["NUGET_PACKAGES"] = _packages;
                var ran = CheckpointFixtureProcess.Run(info, 30_000);
                ran.Exit.ShouldBe(0, "dotnet-cli-home-init");
                File.WriteAllText(marker, "ready\n");
            }

            var listed = new ProcessStartInfo("dotnet");
            listed.ArgumentList.Add("nuget");
            listed.ArgumentList.Add("list");
            listed.ArgumentList.Add("source");
            listed.ArgumentList.Add("--format");
            listed.ArgumentList.Add("detailed");
            var sources = CheckpointFixtureProcess.Run(listed, 30_000);
            sources.Exit.ShouldBe(0, "nuget-list-source");
            var parsed = ParseSources(sources.Stdout);
            if (parsed.Count == 0)
                throw new InvalidOperationException("nuget-restore-sources-unresolved");
            foreach (var source in parsed)
            {
                if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
                    throw new InvalidOperationException(CredentialPrerequisite);
            }

            var mappings = new List<XElement>();
            var settings = new List<XElement>();
            foreach (var path in NugetConfigCandidates())
            {
                if (!File.Exists(path))
                    continue;
                var document = XDocument.Load(path);
                if (document.Descendants().Any(IsCredential))
                    throw new InvalidOperationException(CredentialPrerequisite);
                mappings.AddRange(document.Descendants().Where(element => element.Name.LocalName == "packageSourceMapping"));
                foreach (var add in document.Descendants().Where(element => element.Name.LocalName == "add" && element.Parent?.Name.LocalName == "config"))
                {
                    var key = add.Attribute("key")?.Value ?? "";
                    if (IsSecretKey(key))
                        throw new InvalidOperationException(CredentialPrerequisite);
                    settings.Add(add);
                }
            }

            var configuration = new XElement("configuration",
                new XElement("packageSources",
                    new XElement("clear"),
                    parsed.Select(source => new XElement("add",
                        new XAttribute("key", source.Name),
                        new XAttribute("value", source.Url)))));
            foreach (var mapping in mappings)
                configuration.Add(new XElement(mapping));
            if (settings.Count > 0)
            {
                var config = new XElement("config");
                foreach (var setting in settings)
                    config.Add(new XElement(setting));
                configuration.Add(config);
            }

            _mappingCount = mappings.Count;
            _restoreTemplate = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), configuration).ToString();
            _ready = true;
        }
    }

    internal static IEnumerable<string> NugetConfigCandidates()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(profile))
            yield return Path.Combine(profile, ".nuget", "NuGet", "NuGet.Config");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            yield return Path.Combine(appData, "NuGet", "NuGet.Config");
    }

    internal static List<(string Name, string Url)> ParseSources(string text)
    {
        var result = new List<(string Name, string Url)>();
        string? name = null;
        var enabled = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var open = line.IndexOf('[', StringComparison.Ordinal);
            var close = line.EndsWith(']') ? line.Length - 1 : -1;
            if (open > 0 && close > open && char.IsDigit(line[0]))
            {
                var dot = line.IndexOf('.', StringComparison.Ordinal);
                name = dot > 0 && dot < open ? line[(dot + 1)..open].Trim() : null;
                enabled = line[(open + 1)..close].Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (name is not null && enabled && line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                result.Add((name, line));
                name = null;
            }
        }

        return result;
    }

    private static bool IsCredential(XElement element)
    {
        var name = element.Name.LocalName;
        if (name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || name.Equals("apikeys", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var attribute in element.Attributes())
        {
            var attributeName = attribute.Name.LocalName;
            if (attributeName.Contains("password", StringComparison.OrdinalIgnoreCase)
                || attributeName.Contains("token", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsSecretKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("credential", StringComparison.OrdinalIgnoreCase)
        || key.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private static string[] QueryLocalEnvVars()
    {
        var start = new ProcessStartInfo("git");
        start.ArgumentList.Add("rev-parse");
        start.ArgumentList.Add("--local-env-vars");
        var output = CheckpointFixtureProcess.Run(start, 30_000);
        output.Exit.ShouldBe(0, output.Stderr);
        return output.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed class IsolatedDriver : IConfinedDriver
    {
        private readonly IConfinedDriver _inner;
        private readonly CheckpointFixtureGitEnvironment _owner;

        public IsolatedDriver(IConfinedDriver inner, CheckpointFixtureGitEnvironment owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public BuildConfinement.LedgerCapture CaptureLedger(DriverRequest request) =>
            _inner.CaptureLedger(request);

        public void Kill(bool entireProcessTree) => _inner.Kill(entireProcessTree);

        public Task<DriverResult> RunAsync(DriverRequest request, CancellationToken cancellationToken)
        {
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (request.Environment is not null)
            {
                foreach (var pair in request.Environment)
                    merged[pair.Key] = pair.Value;
            }

            foreach (var pair in _owner._child)
                merged[pair.Key] = pair.Value;
            var arguments = AppendRestore(request.Arguments, _owner.RestoreConfigPath);
            return _inner.RunAsync(request with { Environment = merged, Arguments = arguments }, cancellationToken);
        }

        private static IReadOnlyList<string> AppendRestore(IReadOnlyList<string> arguments, string restoreConfig)
        {
            if (arguments.Count == 0)
                return arguments;
            var verb = arguments[0];
            if (!verb.Equals("build", StringComparison.OrdinalIgnoreCase)
                && !verb.Equals("restore", StringComparison.OrdinalIgnoreCase)
                && !verb.Equals("test", StringComparison.OrdinalIgnoreCase)
                && !verb.Equals("msbuild", StringComparison.OrdinalIgnoreCase))
                return arguments;
            const string prefix = "--property:RestoreConfigFile=";
            foreach (var argument in arguments)
            {
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arguments;
            }

            var copy = new List<string>(arguments) { prefix + restoreConfig };
            return copy;
        }
    }
}
