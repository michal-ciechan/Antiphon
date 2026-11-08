using System.Text;
using System.Text.Json.Nodes;
using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1778 S2. The runner seeds Claude trust from the launch env, else the process env.
/// These witnesses use a temp config file and never touch a real Claude home.
/// </summary>
[Category("Unit")]
public sealed class ClaudeLaunchTrustSeedTests
{
    [Test]
    public void Claude_exe_seeds_the_cwd_key_in_the_request_config_dir()
    {
        using var workspace = TrustWorkspace.Create();
        workspace.WriteProjects(workspace.RequestConfigFile);
        workspace.WriteProjects(workspace.ProcessConfigFile);
        var lines = new List<string>();

        var result = ClaudeLaunchTrustSeed.Apply(
            workspace.Launch("claude", new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = workspace.RequestConfigDir,
            }),
            workspace.ProcessEnv(),
            lines.Add);

        result.ShouldNotBeNull();
        result!.Value.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
        workspace.HasTrust(workspace.RequestConfigFile).ShouldBeTrue();
        workspace.HasTrust(workspace.ProcessConfigFile).ShouldBeFalse();
        lines.Count.ShouldBe(1);
        lines[0].ShouldContain("Seeded");
    }

    [Test]
    public void Process_env_is_the_fallback_when_the_request_names_none()
    {
        using var workspace = TrustWorkspace.Create();
        workspace.WriteProjects(workspace.ProcessConfigFile);

        var result = ClaudeLaunchTrustSeed.Apply(
            workspace.Launch("claude", new Dictionary<string, string>()),
            workspace.ProcessEnv(),
            log: null);

        result.ShouldNotBeNull();
        result!.Value.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
        result.Value.ConfigPath.ShouldBe(workspace.ProcessConfigFile);
        workspace.HasTrust(workspace.ProcessConfigFile).ShouldBeTrue();
        File.Exists(workspace.RequestConfigFile).ShouldBeFalse();
    }

    [Test]
    public void Non_claude_exe_returns_null_and_writes_nothing()
    {
        using var workspace = TrustWorkspace.Create();
        workspace.WriteProjects(workspace.RequestConfigFile);
        var before = File.ReadAllBytes(workspace.RequestConfigFile);
        var env = new Dictionary<string, string>
        {
            ["CLAUDE_CONFIG_DIR"] = workspace.RequestConfigDir,
        };

        foreach (var exe in new[] { "grok", "/usr/local/bin/codex", @"C:\Windows\System32\pwsh.exe" })
        {
            var result = ClaudeLaunchTrustSeed.Apply(workspace.Launch(exe, env), workspace.ProcessEnv(), log: null);
            result.ShouldBeNull(exe);
        }

        File.ReadAllBytes(workspace.RequestConfigFile).ShouldBe(before);
        File.Exists(workspace.ProcessConfigFile).ShouldBeFalse();
    }

    [Test]
    public void Missing_config_file_is_NoConfigFile_and_creates_nothing()
    {
        using var workspace = TrustWorkspace.Create();
        Directory.CreateDirectory(workspace.RequestConfigDir);

        var result = ClaudeLaunchTrustSeed.Apply(
            workspace.Launch("/usr/local/bin/claude", new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = workspace.RequestConfigDir,
            }),
            new Dictionary<string, string?>(),
            log: null);

        result.ShouldNotBeNull();
        result!.Value.Outcome.ShouldBe(ClaudeProjectTrustOutcome.NoConfigFile);
        File.Exists(workspace.RequestConfigFile).ShouldBeFalse();
        Directory.GetFileSystemEntries(workspace.RequestConfigDir).ShouldBeEmpty();
    }

    [Test]
    [Arguments("/usr/local/bin/claude", true)]
    [Arguments(@"C:\x\claude.exe", true)]
    [Arguments("claude.cmd", true)]
    [Arguments("claude-helper", false)]
    [Arguments("/usr/local/bin/claude-helper", false)]
    public void Exe_match_is_by_file_name_only(string exe, bool matches)
    {
        using var workspace = TrustWorkspace.Create();
        workspace.WriteProjects(workspace.RequestConfigFile);
        var before = File.ReadAllBytes(workspace.RequestConfigFile);

        var result = ClaudeLaunchTrustSeed.Apply(
            workspace.Launch(exe, new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = workspace.RequestConfigDir,
            }),
            new Dictionary<string, string?>(),
            log: null);

        if (matches)
        {
            result.ShouldNotBeNull(exe);
            result!.Value.Outcome.ShouldBe(ClaudeProjectTrustOutcome.Seeded);
            workspace.HasTrust(workspace.RequestConfigFile).ShouldBeTrue();
        }
        else
        {
            result.ShouldBeNull(exe);
            File.ReadAllBytes(workspace.RequestConfigFile).ShouldBe(before);
        }
    }

    private sealed class TrustWorkspace : IDisposable
    {
        private readonly string _root;
        private TrustWorkspace(string root) => _root = root;

        public string Cwd { get; private init; } = "";
        public string RequestConfigDir { get; private init; } = "";
        public string ProcessConfigDir { get; private init; } = "";
        public string RequestConfigFile => Path.Combine(RequestConfigDir, ".claude.json");
        public string ProcessConfigFile => Path.Combine(ProcessConfigDir, ".claude.json");

        public static TrustWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "c1778-trust-" + Guid.NewGuid().ToString("N"));
            var cwd = Path.Combine(root, "cwd");
            var requestDir = Path.Combine(root, "request-config");
            var processDir = Path.Combine(root, "process-config");
            Directory.CreateDirectory(cwd);
            Directory.CreateDirectory(requestDir);
            Directory.CreateDirectory(processDir);
            return new TrustWorkspace(root)
            {
                Cwd = cwd,
                RequestConfigDir = requestDir,
                ProcessConfigDir = processDir,
            };
        }

        public void WriteProjects(string path) =>
            File.WriteAllText(path, """{"projects":{}}""", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        public IReadOnlyDictionary<string, string?> ProcessEnv() =>
            new Dictionary<string, string?> { ["CLAUDE_CONFIG_DIR"] = ProcessConfigDir };

        public RunnerLaunchRequest Launch(string exe, IReadOnlyDictionary<string, string> env) =>
            new(Guid.NewGuid(), exe, Array.Empty<string>(), env, Cwd, Cols: 80, Rows: 24);

        public bool HasTrust(string configPath)
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject;
            var key = ClaudeProjectTrust.ProjectKey(Cwd);
            return node?["projects"]?[key]?["hasTrustDialogAccepted"]?.GetValue<bool>() == true;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // The witness already recorded the file state.
            }
        }
    }
}
