using System.Text.RegularExpressions;
using Antiphon.Tests.Scripts;
using Shouldly;
using TUnit.Core;
using YamlDotNet.RepresentationModel;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class EvidencePolicyWorkflowTests
{
    // The guard step's whole run body: one bash command (GitHub runs it with bash -eo pipefail, so its exit code is the
    // step's), every value a double-quoted expansion of an env-bound variable.
    private const string GuardCommand = "./scripts/antiphon evidence-diff --event-path \"$EVIDENCE_EVENT_PATH\" --event-name \"$EVIDENCE_EVENT_NAME\""
        + " --manual-base \"$EVIDENCE_MANUAL_BASE\" --manual-head \"$EVIDENCE_MANUAL_HEAD\"";

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
    private static YamlMappingNode Uses(YamlMappingNode root, string action) =>
        Steps(root).Single(s => Has(s, "uses") && Scalar(Get(s, "uses")).StartsWith(action + "@", StringComparison.Ordinal));
    private static string[] Words(YamlMappingNode guard) => Scalar(Get(guard, "run")).Trim().Split(' ');

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
        var checkout = Uses(Read(), "actions/checkout");
        Scalar(Get(checkout, "uses")).ShouldBe("actions/checkout@v4");
        var inputs = (YamlMappingNode)Get(checkout, "with");
        Scalar(Get(inputs, "fetch-depth")).ShouldBe("0", "c1015-full-history");
        Scalar(Get(inputs, "ref")).ShouldBe("${{ inputs.head || github.event.after || github.sha }}", "c1015-checkout-head");
    }

    [Test]
    public void Workflow_calls_real_guard_and_propagates_failure()
    {
        var root = Read(); var guard = Guard(root);
        Scalar(Get(guard, "shell")).ShouldBe("bash", "c1015-exit-propagation");
        // The single command is the whole body: no `||`, `;`, `&`, pipe, trap or `exit` can replace its exit code.
        Scalar(Get(guard, "run")).Trim().ShouldBe(GuardCommand, "c1015-real-guard-call");
        Scalar(Get(guard, "run")).Trim().Split('\n').Length.ShouldBe(1, "c1015-exit-propagation");
        var words = Words(guard);
        words[0].ShouldBe("./scripts/antiphon", "c1015-real-guard-call");
        words[1].ShouldBe("evidence-diff", "c1015-real-guard-call");
        words[3].ShouldBe("\"$EVIDENCE_EVENT_PATH\"", "c1015-event-path-binding");
        words[5].ShouldBe("\"$EVIDENCE_EVENT_NAME\"", "c1015-event-name-binding");
        // The launcher the step runs is the real one: tracked, LF (a CR would break the shebang on the runner).
        var launcher = File.ReadAllBytes(Path.Combine(EvidenceGitFixture.Source, "scripts", "antiphon"));
        launcher.AsSpan().StartsWith("#!/bin/sh\n"u8).ShouldBeTrue("c1015-real-guard-call");
        launcher.ShouldNotContain((byte)'\r', "c1015-real-guard-call");
        // It builds antiphon-scripts with the pinned SDK, after the checkout.
        var steps = Steps(root).ToList();
        var setup = Uses(root, "actions/setup-dotnet");
        Scalar(Get(setup, "uses")).ShouldBe("actions/setup-dotnet@v4");
        Scalar(Get((YamlMappingNode)Get(setup, "with"), "global-json-file")).ShouldBe("global.json");
        steps.IndexOf(Uses(root, "actions/checkout")).ShouldBeLessThan(steps.IndexOf(setup));
        steps.IndexOf(setup).ShouldBeLessThan(steps.IndexOf(guard));
        Has(Job(root), "continue-on-error").ShouldBeFalse("c1015-no-error-waiver");
        foreach (var step in Steps(root)) Has(step, "continue-on-error").ShouldBeFalse("c1015-no-error-waiver");
    }

    [Test]
    public void Workflow_uses_data_arguments_and_no_write_permissions()
    {
        var root = Read(); var guard = Guard(root); var run = Scalar(Get(guard, "run"));
        run.ShouldNotContain("${{", customMessage: "c1015-no-event-shell");
        var env = (YamlMappingNode)Get(guard, "env");
        Scalar(Get(env, "EVIDENCE_EVENT_PATH")).ShouldBe("${{ github.event_path }}", "c1015-event-path-binding");
        Scalar(Get(env, "EVIDENCE_EVENT_NAME")).ShouldBe("${{ github.event_name }}", "c1015-event-name-binding");
        Scalar(Get(env, "EVIDENCE_MANUAL_BASE")).ShouldBe("${{ inputs.base }}", "c1015-manual-base-binding");
        var words = Words(guard);
        words[7].ShouldBe("\"$EVIDENCE_MANUAL_BASE\"", "c1015-manual-base-binding");
        // Every argument after the subcommand is a flag followed by one double-quoted expansion of a variable this step
        // binds: data only, no word splitting, globbing or command substitution.
        (words.Length % 2).ShouldBe(0, "c1015-data-arguments");
        for (var i = 2; i < words.Length; i += 2)
        {
            words[i].ShouldStartWith("--", customMessage: "c1015-data-arguments");
            var value = Regex.Match(words[i + 1], "^\"\\$([A-Z_]+)\"$");
            value.Success.ShouldBeTrue("c1015-data-arguments: " + words[i + 1]);
            Has(env, value.Groups[1].Value).ShouldBeTrue("c1015-data-arguments: " + value.Groups[1].Value);
        }
        var permissions = (YamlMappingNode)Get(root, "permissions");
        Scalar(Get(permissions, "contents")).ShouldBe("read", "c1015-read-permission");
        permissions.Children.Count.ShouldBe(1, "c1015-read-permission");
        Has(Job(root), "permissions").ShouldBeFalse("c1015-read-permission");
    }
}
