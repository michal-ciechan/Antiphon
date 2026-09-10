using System.Text;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class GrokRulesCompositionTests
{
    [Test]
    public async Task C470_mutation_rules_payload_contains_its_stage()
    {
        var (dispatcher, provider) = GrokRulesLaunchRefusalTests.CreateDelegateHarness();
        await using var owned = provider;
        var task = GrokRulesLaunchRefusalTests.TaskFor(AgentTaskKind.Worker, AgentTaskRole.Mutation);
        var spec = GrokRulesLaunchRefusalTests.SpecOf(dispatcher, task, AgentKind.Grok, null);
        var text = spec.GrokRulesPayload.ShouldNotBeNull().Content;
        text.ShouldStartWith("[bundle:stage-mutation v");
        text.ShouldNotContain("[bundle:stage-code");
        text.ShouldNotContain("[bundle:stage-review");
        spec.Args.ShouldAllBe(a => !a.Contains("[bundle:"));
        spec.Env.Values.ShouldAllBe(v => !v.Contains("[bundle:"));
        await AssertExactDiskAsync(spec.GrokRulesPayload, text);
    }

    [Test]
    [Arguments(AgentTaskRole.Investigate)]
    [Arguments(AgentTaskRole.Plan)]
    [Arguments(AgentTaskRole.TestDesign)]
    [Arguments(AgentTaskRole.Mutation)]
    [Arguments(AgentTaskRole.Code)]
    [Arguments(AgentTaskRole.Review)]
    public async Task Worker_and_stage_bundles_reach_typed_rules_payload_without_argv_text(AgentTaskRole role)
    {
        var (dispatcher, provider) = GrokRulesLaunchRefusalTests.CreateDelegateHarness();
        await using var owned = provider;
        var task = GrokRulesLaunchRefusalTests.TaskFor(AgentTaskKind.Worker, role);
        var expected = InstructionBundleComposer.Compose(InstructionBundles.ForDelegate(task.Kind, task.Role));
        var spec = Should.NotThrow(() => GrokRulesLaunchRefusalTests.SpecOf(dispatcher, task, AgentKind.Grok, null));
        spec.GrokRulesPayload.ShouldNotBeNull().Content.ShouldBe(expected.Text);
        spec.GrokRulesPayload.Content.ShouldContain(InstructionBundles.Get(InstructionBundles.DelegateBasics).Text);
        spec.GrokRulesPayload.Content.ShouldContain(InstructionBundles.Get(InstructionBundles.StageKeyFor(role)).Text);
        spec.Args.ShouldAllBe(a => !a.Contains("[bundle:") && !a.Contains(expected.Text));
        spec.Env.Values.ShouldAllBe(v => !v.Contains("[bundle:") && !v.Contains(expected.Text));
        spec.Args.ShouldNotContain("--agent");
        await AssertExactDiskAsync(spec.GrokRulesPayload, expected.Text);
    }

    [Test]
    [Arguments(SessionBackend.PtyHost)]
    [Arguments(SessionBackend.Herdr)]
    public async Task Standing_channel_composition_preserves_attachment_style_append_and_preamble_bytes(SessionBackend backend)
    {
        await using var h = await BridgeQueueHarness.CreateAsync(new() { ConfigureServices = services =>
            services.AddSingleton<IOptionsMonitor<AgentRegistrySettings>>(new BridgeQueueHarness.OptionsMonitorStub<AgentRegistrySettings>(new() {
                DefaultDefinition = "grok", GrokCredentialProbeEnabled = false,
                Definitions = { ["grok"] = new AgentDefinition { Kind = "Grok", Exe = "grok" } } })) });
        await h.BindChannelAsync();
        await using var db = BridgeQueueHarness.CreateContext();
        var agent = await db.Agents.SingleAsync(a => a.Id == h.AgentId);
        agent.Kind = AgentKind.Grok;
        agent.SessionBackend = backend;
        agent.ReplyStyle = AgentReplyStyle.Explanatory;
        agent.SystemPromptAppend = ChannelPreamble.TelegramPresetTemplate + "\r\nCOMPOSE-FIRST $` café 😀\r\n"
            + string.Join("\r\n", Enumerable.Range(1, 1105).Select(i => $"COMPOSE-LINE-{i:D4}: " + new string('x', 40))) + "\r\nCOMPOSE-LAST";
        await AgentBundleAttachments.SetAsync(db, agent, [InstructionBundles.Orchestrator], DateTime.UtcNow, CancellationToken.None);
        await db.SaveChangesAsync();
        var raw = InstructionBundleComposer.Compose([InstructionBundles.Orchestrator], AgentReplyStyles.ComposedKey(agent.ReplyStyle), agent.SystemPromptAppend);
        var expected = ChannelPreamble.Render(raw.Text, agent.Name, [("telegram", "Bound channel (test)")]);
        var composer = new AgentSessionLaunchComposer(db, Options.Create(new DelegationSettings { CommandLineBudgetChars = 1000 }),
            h.Provider.GetRequiredService<AgentRegistry>(), NullLogger<AgentSessionLaunchComposer>.Instance);
        var result = await composer.ComposeForAgentAsync(agent, CancellationToken.None);
        result.GrokRulesPayload.ShouldNotBeNull().Content.ShouldBe(expected);
        result.ComposedStamp.ShouldBe(raw.StampLine);
        result.GrokRulesPayload.Content.Length.ShouldBeGreaterThan(30000);
        result.GrokRulesPayload.Content.ShouldContain("telegram \"Bound channel (test)\"");
        result.ExtraArgs.ShouldAllBe(a => !a.Contains("COMPOSE-FIRST") && !a.Contains("COMPOSE-LAST"));
        result.ExtraEnv.Values.ShouldAllBe(v => !v.Contains("COMPOSE-FIRST") && !v.Contains("COMPOSE-LAST"));
        await AssertExactDiskAsync(result.GrokRulesPayload, expected);
    }

    private static async Task AssertExactDiskAsync(GrokRulesPayload payload, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "card0395-composition", Guid.NewGuid().ToString("N"));
        try
        {
            var id = Guid.NewGuid();
            var receipt = await new GrokRulesFileStore(root, new()).WriteAsync(id, payload, CancellationToken.None);
            (await File.ReadAllBytesAsync(receipt.Path)).ShouldBe(new UTF8Encoding(false, true).GetBytes(expected));
            var bootstrap = GrokRulesTransport.Bootstrap(receipt.Path);
            bootstrap.ShouldNotContain(receipt.Sha256);
            bootstrap.ShouldNotContain(payload.Generation.ToString("N"));
            GrokRulesArgvPolicy.ValidatePayload(bootstrap, true, true).ShouldBeNull();
            GrokRulesArgvPolicy.ValidatePayload(bootstrap, false, true).ShouldBeNull();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
