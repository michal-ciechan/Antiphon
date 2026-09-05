using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0251 S1: marker schema, home-config readers (including the key-form traps), and
/// <see cref="OrchestratorWorkspaceLayout.Classify"/> over fixture directories — one row
/// per state, each CLI, and the nested-for-Claude Warning.
/// </summary>
[Category("Card0251")]
[Category("Unit")]
public class OrchestratorWorkspaceLayoutTests
{
    private static readonly Guid ProjectId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly OrchestratorWorkspaceFactGatherer Gatherer = new();

    // ---- marker schema -------------------------------------------------------------------------

    [Test]
    public void Parses_schema_v1_marker()
    {
        var json = """
            { "version": 1, "checkout": "../gym-stat", "project": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "cli": "claude" }
            """;
        OrchestratorWorkspaceLayout.TryParseMarker(json, out var marker).ShouldBeTrue();
        marker.Version.ShouldBe(1);
        marker.Checkout.ShouldBe("../gym-stat");
        marker.Project.ShouldBe(ProjectId);
        marker.Cli.ShouldBe("claude");
    }

    [Test]
    public void Rejects_wrong_version_or_missing_checkout()
    {
        OrchestratorWorkspaceLayout.TryParseMarker("""{"version":2,"checkout":"../x"}""", out _).ShouldBeFalse();
        OrchestratorWorkspaceLayout.TryParseMarker("""{"version":1,"checkout":""}""", out _).ShouldBeFalse();
        OrchestratorWorkspaceLayout.TryParseMarker("""{"version":1}""", out _).ShouldBeFalse();
        OrchestratorWorkspaceLayout.TryParseMarker("not json", out _).ShouldBeFalse();
        OrchestratorWorkspaceLayout.TryParseMarker(null, out _).ShouldBeFalse();
    }

    // ---- home readers / key-form traps ---------------------------------------------------------

    [Test]
    public void Claude_forward_slash_key_is_the_only_approval()
    {
        using var dir = new TempDir("claude-key");
        var slash = OrchestratorWorkspaceLayout.ForwardSlashKey(dir.Path);
        var backslash = Path.GetFullPath(dir.Path).TrimEnd('\\', '/');

        var approved = $$"""
            { "projects": { "{{slash}}": { "hasClaudeMdExternalIncludesApproved": true } } }
            """;
        OrchestratorWorkspaceLayout.ReadClaudeExternalIncludes(approved, dir.Path)
            .ShouldBe(ClaudeExternalIncludesApproval.Approved);

        var escapedBackslash = JsonSerializer.Serialize(backslash);
        var trap = $$"""
            { "projects": { {{escapedBackslash}}: { "hasClaudeMdExternalIncludesApproved": true } } }
            """;
        OrchestratorWorkspaceLayout.ReadClaudeExternalIncludes(trap, dir.Path)
            .ShouldBe(ClaudeExternalIncludesApproval.Absent,
                "a backslash project key is the CARD-0251 trap — Claude looks up the forward-slash form");

        var declined = $$"""
            { "projects": { "{{slash}}": { "hasClaudeMdExternalIncludesApproved": false } } }
            """;
        OrchestratorWorkspaceLayout.ReadClaudeExternalIncludes(declined, dir.Path)
            .ShouldBe(ClaudeExternalIncludesApproval.Declined);

        OrchestratorWorkspaceLayout.ReadClaudeExternalIncludes("""{"projects":{}}""", dir.Path)
            .ShouldBe(ClaudeExternalIncludesApproval.Absent);
    }

    [Test]
    public void Codex_lower_case_backslash_key_is_the_only_trust()
    {
        using var dir = new TempDir("codex-key");
        var lower = OrchestratorWorkspaceLayout.LowerBackslashKey(dir.Path);
        var mixed = Path.GetFullPath(dir.Path).TrimEnd('/', '\\');
        var forward = mixed.Replace('\\', '/').ToLowerInvariant();

        var trusted = $"""
            [projects.'{lower}']
            trust_level = "trusted"
            """;
        OrchestratorWorkspaceLayout.ReadCodexTrusted(trusted, dir.Path).ShouldBeTrue();

        var mixedTrap = $"""
            [projects.'{mixed}']
            trust_level = "trusted"
            """;
        if (!string.Equals(mixed, lower, StringComparison.Ordinal))
        {
            OrchestratorWorkspaceLayout.ReadCodexTrusted(mixedTrap, dir.Path)
                .ShouldBeFalse("mixed-case Codex project keys do not match");
        }

        var slashTrap = $"""
            [projects.'{forward}']
            trust_level = "trusted"
            """;
        OrchestratorWorkspaceLayout.ReadCodexTrusted(slashTrap, dir.Path)
            .ShouldBeFalse("forward-slash Codex project keys do not match");

        var untrusted = $"""
            [projects.'{lower}']
            trust_level = "untrusted"
            """;
        OrchestratorWorkspaceLayout.ReadCodexTrusted(untrusted, dir.Path).ShouldBeFalse();
    }

    [Test]
    public void Grok_trusts_the_exact_folder_in_either_slash_form_not_a_parent()
    {
        using var dir = new TempDir("grok-key");
        var back = Path.GetFullPath(dir.Path).TrimEnd('/', '\\');
        var forward = back.Replace('\\', '/');

        OrchestratorWorkspaceLayout.ReadGrokTrusted(
            $"""
            [folders.'{back}']
            trusted = true
            """, dir.Path).ShouldBeTrue();

        OrchestratorWorkspaceLayout.ReadGrokTrusted(
            $"""
            [folders.'{forward}']
            trusted = true
            """, dir.Path).ShouldBeTrue("CARD-0315: either slash form is an exact match");

        var parent = Path.GetDirectoryName(back)!;
        OrchestratorWorkspaceLayout.ReadGrokTrusted(
            $"""
            [folders.'{parent}']
            trusted = true
            """, dir.Path).ShouldBeFalse("a parent folder is a different workspace");
    }

    [Test]
    public void proposed_sibling_path_is_beside_the_checkout()
    {
        using var dir = new TempDir("sibpath");
        var checkout = Path.Combine(dir.Path, "gym-stat");
        Directory.CreateDirectory(checkout);
        OrchestratorWorkspaceLayout.ProposedSiblingPath(checkout)
            .ShouldBe(Path.Combine(dir.Path, "gym-stat-orchestrator"));
    }

    [Test]
    public void cli_from_kind_maps_the_three_orchestrator_clis()
    {
        OrchestratorWorkspaceLayout.CliFromKind(AgentKind.ClaudeCode)
            .ShouldBe(OrchestratorWorkspaceCli.Claude);
        OrchestratorWorkspaceLayout.CliFromKind(AgentKind.Codex)
            .ShouldBe(OrchestratorWorkspaceCli.Codex);
        OrchestratorWorkspaceLayout.CliFromKind(AgentKind.Grok)
            .ShouldBe(OrchestratorWorkspaceCli.Grok);
        OrchestratorWorkspaceLayout.CliFromKind(AgentKind.Raw)
            .ShouldBe(OrchestratorWorkspaceCli.Claude);
    }

    [Test]
    public async Task follow_marker_returns_checkout_when_the_marker_resolves()
    {
        using var fx = await SiblingFixture.CreateAsync(OrchestratorWorkspaceCli.Claude);
        OrchestratorWorkspaceFactGatherer.FollowMarkerOrSelf(fx.Orch)
            .ShouldBe(Path.GetFullPath(fx.Repo));
        OrchestratorWorkspaceFactGatherer.FollowMarkerOrSelf(fx.Repo)
            .ShouldBe(Path.GetFullPath(fx.Repo));
    }

    // ---- Classify over fixture directories -----------------------------------------------------

    [Test]
    [Arguments(OrchestratorWorkspaceCli.Claude)]
    [Arguments(OrchestratorWorkspaceCli.Codex)]
    [Arguments(OrchestratorWorkspaceCli.Grok)]
    public async Task Sibling_dedicated_layout_is_Dedicated_when_the_precondition_holds(
        OrchestratorWorkspaceCli cli)
    {
        using var fx = await SiblingFixture.CreateAsync(cli);
        var facts = await Gatherer.GatherAsync(fx.Orch, cli);
        var home = ApprovedHome(fx.Orch, cli);
        OrchestratorWorkspaceLayout.Classify(facts, cli, home)
            .ShouldBe(OrchestratorWorkspaceState.Dedicated);
        facts.DirectoryGitToplevel.ShouldBeNull("the sibling orch folder is not a git repo");
        facts.CheckoutExists.ShouldBeTrue();
    }

    [Test]
    [Arguments(OrchestratorWorkspaceCli.Claude)]
    [Arguments(OrchestratorWorkspaceCli.Codex)]
    [Arguments(OrchestratorWorkspaceCli.Grok)]
    public async Task Sibling_dedicated_layout_is_DedicatedUnapproved_without_the_precondition(
        OrchestratorWorkspaceCli cli)
    {
        using var fx = await SiblingFixture.CreateAsync(cli);
        var facts = await Gatherer.GatherAsync(fx.Orch, cli);
        OrchestratorWorkspaceLayout.Classify(facts, cli, OrchestratorWorkspaceHomeState.None)
            .ShouldBe(OrchestratorWorkspaceState.DedicatedUnapproved);
    }

    [Test]
    public async Task Nested_checkout_under_the_orch_folder_is_DedicatedNested_for_Claude()
    {
        using var fx = await NestedFixture.CreateAsync(OrchestratorWorkspaceCli.Claude);
        var facts = await Gatherer.GatherAsync(fx.Orch, OrchestratorWorkspaceCli.Claude);
        facts.CheckoutExists.ShouldBeTrue();
        OrchestratorWorkspaceLayout.IsWithin(facts.ResolvedCheckout!, fx.Orch).ShouldBeTrue();
        OrchestratorWorkspaceLayout.Classify(
                facts, OrchestratorWorkspaceCli.Claude, ApprovedHome(fx.Orch, OrchestratorWorkspaceCli.Claude))
            .ShouldBe(OrchestratorWorkspaceState.DedicatedNested,
                "the nested shape injects the orch CLAUDE.md into every checkout session");
    }

    [Test]
    public async Task Claude_declined_external_includes_is_DedicatedUnapproved_not_absent()
    {
        using var fx = await SiblingFixture.CreateAsync(OrchestratorWorkspaceCli.Claude);
        var facts = await Gatherer.GatherAsync(fx.Orch, OrchestratorWorkspaceCli.Claude);
        var home = new OrchestratorWorkspaceHomeState(
            ClaudeExternalIncludesApproval.Declined, CodexTrusted: false, GrokTrusted: false);
        OrchestratorWorkspaceLayout.Classify(facts, OrchestratorWorkspaceCli.Claude, home)
            .ShouldBe(OrchestratorWorkspaceState.DedicatedUnapproved);
    }

    [Test]
    public async Task Git_checkout_with_instruction_files_is_CheckoutAsCwd()
    {
        using var repo = await GitDir.CreateAsync("checkout-cwd");
        await File.WriteAllTextAsync(Path.Combine(repo.Path, "AGENTS.md"), "# repo\n");
        var facts = await Gatherer.GatherAsync(repo.Path, OrchestratorWorkspaceCli.Claude);
        facts.GitRootHasInstructionArtifacts.ShouldBeTrue();
        OrchestratorWorkspaceLayout.Classify(facts, OrchestratorWorkspaceCli.Claude, OrchestratorWorkspaceHomeState.None)
            .ShouldBe(OrchestratorWorkspaceState.CheckoutAsCwd);
    }

    [Test]
    public async Task Git_checkout_with_no_instruction_files_is_Unconfigured()
    {
        using var repo = await GitDir.CreateAsync("unconfigured");
        var facts = await Gatherer.GatherAsync(repo.Path, OrchestratorWorkspaceCli.Claude);
        facts.GitRootHasInstructionArtifacts.ShouldBeFalse();
        OrchestratorWorkspaceLayout.Classify(facts, OrchestratorWorkspaceCli.Claude, OrchestratorWorkspaceHomeState.None)
            .ShouldBe(OrchestratorWorkspaceState.Unconfigured);
    }

    [Test]
    public async Task Random_directory_is_Foreign()
    {
        using var dir = new TempDir("foreign");
        var facts = await Gatherer.GatherAsync(dir.Path, OrchestratorWorkspaceCli.Claude);
        OrchestratorWorkspaceLayout.Classify(facts, OrchestratorWorkspaceCli.Claude, OrchestratorWorkspaceHomeState.None)
            .ShouldBe(OrchestratorWorkspaceState.Foreign);
    }

    [Test]
    public async Task Gatherer_home_reader_wires_the_three_CLI_files()
    {
        using var dir = new TempDir("home-wire");
        var slash = OrchestratorWorkspaceLayout.ForwardSlashKey(dir.Path);
        var lower = OrchestratorWorkspaceLayout.LowerBackslashKey(dir.Path);
        var back = Path.GetFullPath(dir.Path).TrimEnd('/', '\\');
        var home = Gatherer.ReadHome(
            dir.Path,
            $$"""{ "projects": { "{{slash}}": { "hasClaudeMdExternalIncludesApproved": true } } }""",
            $"[projects.'{lower}']\ntrust_level = \"trusted\"\n",
            $"[folders.'{back}']\ntrusted = true\n");
        home.PreconditionHolds(OrchestratorWorkspaceCli.Claude).ShouldBeTrue();
        home.PreconditionHolds(OrchestratorWorkspaceCli.Codex).ShouldBeTrue();
        home.PreconditionHolds(OrchestratorWorkspaceCli.Grok).ShouldBeTrue();
    }

    [Test]
    public void Classify_is_pure_over_constructed_facts()
    {
        var dedicated = new OrchestratorWorkspaceDirectoryFacts(
            Path: @"C:\src\gym-stat-orchestrator",
            Marker: new OrchestratorWorkspaceMarker(1, "../gym-stat", ProjectId, "claude"),
            ResolvedCheckout: @"C:\src\gym-stat",
            CheckoutExists: true,
            DirectoryGitToplevel: null,
            CheckoutGitToplevel: @"C:\src\gym-stat",
            ContextFileExists: true,
            ContextFileNamesCheckoutAgents: true,
            GitRootHasInstructionArtifacts: false);
        var home = new OrchestratorWorkspaceHomeState(
            ClaudeExternalIncludesApproval.Approved, CodexTrusted: false, GrokTrusted: false);
        OrchestratorWorkspaceLayout.Classify(dedicated, OrchestratorWorkspaceCli.Claude, home)
            .ShouldBe(OrchestratorWorkspaceState.Dedicated);

        var nested = dedicated with
        {
            Path = @"C:\src\gym-stat-orchestrator",
            ResolvedCheckout = @"C:\src\gym-stat-orchestrator\source\repo",
            CheckoutGitToplevel = @"C:\src\gym-stat-orchestrator\source\repo",
        };
        OrchestratorWorkspaceLayout.Classify(nested, OrchestratorWorkspaceCli.Claude, home)
            .ShouldBe(OrchestratorWorkspaceState.DedicatedNested);
    }

    private static OrchestratorWorkspaceHomeState ApprovedHome(string directory, OrchestratorWorkspaceCli cli) =>
        cli switch
        {
            OrchestratorWorkspaceCli.Claude => new(
                ClaudeExternalIncludesApproval.Approved, CodexTrusted: false, GrokTrusted: false),
            OrchestratorWorkspaceCli.Codex => new(
                ClaudeExternalIncludesApproval.Absent, CodexTrusted: true, GrokTrusted: false),
            OrchestratorWorkspaceCli.Grok => new(
                ClaudeExternalIncludesApproval.Absent, CodexTrusted: false, GrokTrusted: true),
            _ => OrchestratorWorkspaceHomeState.None,
        };

    private static string MarkerJson(string checkout, string cli) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            checkout,
            project = ProjectId,
            cli,
        });

    private static string CliName(OrchestratorWorkspaceCli cli) => cli switch
    {
        OrchestratorWorkspaceCli.Claude => "claude",
        OrchestratorWorkspaceCli.Codex => "codex",
        OrchestratorWorkspaceCli.Grok => "grok",
        _ => "claude",
    };

    private static async Task GitInitAsync(string dir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("init");
        psi.ArgumentList.Add("-b");
        psi.ArgumentList.Add("master");
        psi.ArgumentList.Add("-q");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("git init failed to start");
        await p.WaitForExitAsync();
        p.ExitCode.ShouldBe(0, "git init must succeed in " + dir);
    }

    private static void BestEffortDelete(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir(string prefix) =>
            Path = Directory.CreateTempSubdirectory("antiphon-0251-" + prefix + "-").FullName;

        public void Dispose() => BestEffortDelete(Path);
    }

    private sealed class GitDir : IDisposable
    {
        public string Path { get; }

        private GitDir(string path) => Path = path;

        public static async Task<GitDir> CreateAsync(string prefix)
        {
            var path = Directory.CreateTempSubdirectory("antiphon-0251-" + prefix + "-").FullName;
            await GitInitAsync(path);
            return new GitDir(path);
        }

        public void Dispose() => BestEffortDelete(Path);
    }

    /// <summary>
    /// Sibling <c>orch\</c> beside <c>repo\</c> (the CARD-0251 good state). Checkout never
    /// sits inside the orch folder.
    /// </summary>
    private sealed class SiblingFixture : IDisposable
    {
        public string Root { get; }
        public string Orch { get; }
        public string Repo { get; }

        private SiblingFixture(string root, string orch, string repo)
        {
            Root = root;
            Orch = orch;
            Repo = repo;
        }

        public static async Task<SiblingFixture> CreateAsync(OrchestratorWorkspaceCli cli)
        {
            var root = Directory.CreateTempSubdirectory("antiphon-0251-sibfix-").FullName;
            var orch = System.IO.Path.Combine(root, "orch");
            var repo = System.IO.Path.Combine(root, "repo");
            Directory.CreateDirectory(orch);
            Directory.CreateDirectory(repo);
            await GitInitAsync(repo);
            await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "AGENTS.md"), "# checkout\n");
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(orch, OrchestratorWorkspaceLayout.MarkerFileName),
                MarkerJson("../repo", CliName(cli)));
            if (cli == OrchestratorWorkspaceCli.Claude)
            {
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(orch, "CLAUDE.md"),
                    "You are the orchestrator.\n@../repo/AGENTS.md\n");
            }
            else
            {
                await File.WriteAllTextAsync(
                    System.IO.Path.Combine(orch, "AGENTS.md"),
                    "You are the orchestrator. At session start read ../repo/AGENTS.md.\n");
            }

            return new SiblingFixture(root, orch, repo);
        }

        public void Dispose() => BestEffortDelete(Root);
    }

    /// <summary>
    /// Nested <c>orch\source\repo</c> — the shape that is unsafe for Claude Code.
    /// </summary>
    private sealed class NestedFixture : IDisposable
    {
        public string Root { get; }
        public string Orch { get; }
        public string Repo { get; }

        private NestedFixture(string root, string orch, string repo)
        {
            Root = root;
            Orch = orch;
            Repo = repo;
        }

        public static async Task<NestedFixture> CreateAsync(OrchestratorWorkspaceCli cli)
        {
            var root = Directory.CreateTempSubdirectory("antiphon-0251-nestfix-").FullName;
            var orch = System.IO.Path.Combine(root, "orch");
            var repo = System.IO.Path.Combine(orch, "source", "repo");
            Directory.CreateDirectory(repo);
            await GitInitAsync(repo);
            await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "AGENTS.md"), "# checkout\n");
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(orch, OrchestratorWorkspaceLayout.MarkerFileName),
                MarkerJson("source/repo", CliName(cli)));
            await File.WriteAllTextAsync(
                System.IO.Path.Combine(orch, "CLAUDE.md"),
                "You are the orchestrator.\n@source/repo/AGENTS.md\n");
            return new NestedFixture(root, orch, repo);
        }

        public void Dispose() => BestEffortDelete(Root);
    }
}
