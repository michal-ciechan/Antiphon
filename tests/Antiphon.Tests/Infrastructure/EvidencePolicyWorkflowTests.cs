using System.Text;
using System.Text.Json;
using Antiphon.Tests.Scripts;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class EvidencePolicyWorkflowTests
{
    private static YamlMappingNode Read()
    {
        var path = Path.Combine(EvidenceGitFixture.Source, ".github", "workflows", "evidence-policy.yml");
        File.Exists(path).ShouldBeTrue("c1015-workflow-installed");
        var yaml = new YamlStream();
        using var reader = File.OpenText(path);
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents.Single().RootNode;
    }
    private static YamlNode Get(YamlMappingNode map, string key) => map.Children[new YamlScalarNode(key)];
    private static bool Has(YamlMappingNode map, string key) => map.Children.ContainsKey(new YamlScalarNode(key));
    private static string Scalar(YamlNode node) => ((YamlScalarNode)node).Value!;
    private static YamlMappingNode Job(YamlMappingNode root) => (YamlMappingNode)Get((YamlMappingNode)Get(root, "jobs"), "evidence");
    private static YamlMappingNode[] Steps(YamlMappingNode root) => ((YamlSequenceNode)Get(Job(root), "steps")).Children.Cast<YamlMappingNode>().ToArray();
    private static YamlMappingNode Guard(YamlMappingNode root) => Steps(root).Single(s => Has(s, "run"));

    private static async Task<JsonDocument> AstAsync(string source)
    {
        await using var f = await EvidenceGitFixture.CreateAsync();
        var body = "$tokens=$null; $errors=$null; $ast=[System.Management.Automation.Language.Parser]::ParseInput(" + EvidenceGitFixture.Quote(source) + ",[ref]$tokens,[ref]$errors); " +
            "@{errors=@($errors | ForEach-Object Message); commands=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.CommandAst]},$true) | ForEach-Object { ,@($_.CommandElements | ForEach-Object { $_.Extent.Text }) }); exits=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.ExitStatementAst]},$true) | ForEach-Object { $_.Pipeline.Extent.Text })} | ConvertTo-Json -Depth 8 -Compress";
        var r = await f.RunAsync("pwsh", ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(body))]);
        r.Exit.ShouldBe(0, r.Output);
        var json = JsonDocument.Parse(r.Output);
        json.RootElement.GetProperty("errors").GetArrayLength().ShouldBe(0, "real workflow body parses without execution");
        return json;
    }

    [Test]
    public void Workflow_selects_every_required_event()
    {
        var root = Read();
        var events = (YamlMappingNode)Get(root, "on");
        Has(events, "push").ShouldBeTrue("c1015-trigger-master");
        var push = (YamlMappingNode)Get(events, "push");
        var branches = ((YamlSequenceNode)Get(push, "branches")).Children.Select(Scalar).ToArray();
        branches.ShouldContain("master", "c1015-trigger-master");
        branches.ShouldContain("feat/**", "c1015-trigger-feature");
        Has(events, "workflow_dispatch").ShouldBeTrue("c1015-trigger-manual");
        Has(push, "paths").ShouldBeFalse("c1015-no-path-filter");
        Has(push, "paths-ignore").ShouldBeFalse("c1015-no-path-filter");
        Has(Job(root), "if").ShouldBeFalse("c1015-no-path-filter");
        foreach (var step in Steps(root)) Has(step, "if").ShouldBeFalse("c1015-no-path-filter");
    }

    [Test]
    public void Workflow_pins_full_history_and_event_head()
    {
        var checkout = Steps(Read()).Single(s => Has(s, "uses"));
        Scalar(Get(checkout, "uses")).ShouldBe("actions/checkout@v4");
        var inputs = (YamlMappingNode)Get(checkout, "with");
        Scalar(Get(inputs, "fetch-depth")).ShouldBe("0", "c1015-full-history");
        Scalar(Get(inputs, "ref")).ShouldBe("${{ inputs.head || github.event.after || github.sha }}", "c1015-checkout-head");
    }

    [Test]
    public async Task Workflow_calls_real_guard_and_propagates_failure()
    {
        var root = Read(); var guard = Guard(root);
        Scalar(Get(guard, "shell")).ShouldBe("pwsh");
        using var ast = await AstAsync(Scalar(Get(guard, "run")));
        var commands = ast.RootElement.GetProperty("commands").EnumerateArray().ToArray();
        commands.Length.ShouldBe(1, "c1015-real-guard-call");
        var elements = commands.Single().EnumerateArray().Select(e => e.GetString()).ToArray();
        elements[0].ShouldBe("./scripts/check-evidence-diff.ps1", "c1015-real-guard-call");
        elements.ShouldBe(new[] { "./scripts/check-evidence-diff.ps1", "-EventPath", "$env:EVIDENCE_EVENT_PATH", "-EventName", "$env:EVIDENCE_EVENT_NAME", "-ManualBase", "$env:EVIDENCE_MANUAL_BASE", "-ManualHead", "$env:EVIDENCE_MANUAL_HEAD" }, "c1015-real-guard-call");
        elements[2].ShouldBe("$env:EVIDENCE_EVENT_PATH", "c1015-event-path-binding");
        elements[4].ShouldBe("$env:EVIDENCE_EVENT_NAME", "c1015-event-name-binding");
        ast.RootElement.GetProperty("exits").EnumerateArray().Select(e => e.GetString()).ToArray()
            .ShouldBe(new[] { "$LASTEXITCODE" }, "c1015-exit-propagation");
        Has(Job(root), "continue-on-error").ShouldBeFalse("c1015-no-error-waiver");
        foreach (var step in Steps(root)) Has(step, "continue-on-error").ShouldBeFalse("c1015-no-error-waiver");
    }

    [Test]
    public async Task Workflow_uses_data_arguments_and_no_write_permissions()
    {
        var root = Read(); var guard = Guard(root); var run = Scalar(Get(guard, "run"));
        run.ShouldNotContain("${{", customMessage: "c1015-no-event-shell");
        using var ast = await AstAsync(run);
        var env = (YamlMappingNode)Get(guard, "env");
        Scalar(Get(env, "EVIDENCE_EVENT_PATH")).ShouldBe("${{ github.event_path }}", "c1015-event-path-binding");
        Scalar(Get(env, "EVIDENCE_EVENT_NAME")).ShouldBe("${{ github.event_name }}", "c1015-event-name-binding");
        Scalar(Get(env, "EVIDENCE_MANUAL_BASE")).ShouldBe("${{ inputs.base }}", "c1015-manual-base-binding");
        var elements = ast.RootElement.GetProperty("commands").EnumerateArray().Single().EnumerateArray().Select(e => e.GetString()).ToArray();
        elements[6].ShouldBe("$env:EVIDENCE_MANUAL_BASE", "c1015-manual-base-binding");
        var permissions = (YamlMappingNode)Get(root, "permissions");
        Scalar(Get(permissions, "contents")).ShouldBe("read", "c1015-read-permission");
        permissions.Children.Count.ShouldBe(1, "c1015-read-permission");
        Has(Job(root), "permissions").ShouldBeFalse("c1015-read-permission");
    }
}
