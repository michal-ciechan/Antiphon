using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public class ChannelOutboundPolicyTests
{
    [Test]
    public void Profile_binding_defaults_and_validation()
    {
        var settings = new ChannelOutboundSettings();
        settings.Profiles.ShouldBeEmpty();
        new ChatChannel().OutboundAgentProfile.ShouldBeNull();

        var validator = new ChannelOutboundSettingsValidator();
        validator.Validate(null, settings).Succeeded.ShouldBeTrue();

        foreach (var timeout in new[] { 9, 301 })
        {
            var bad = new ChannelOutboundSettings
            {
                Profiles =
                {
                    ["pdf"] = new ChannelOutboundProfileSettings
                    {
                        ProjectId = Guid.NewGuid(),
                        AgentId = Guid.NewGuid(),
                        PromptFile = "prompts/pdf.md",
                        TimeoutSeconds = timeout,
                    },
                },
            };
            validator.Validate(null, bad).Succeeded.ShouldBeFalse();
        }

        foreach (var timeout in new[] { 10, 120, 300 })
        {
            var ok = new ChannelOutboundSettings
            {
                Profiles =
                {
                    ["pdf"] = new ChannelOutboundProfileSettings
                    {
                        ProjectId = Guid.NewGuid(),
                        AgentId = Guid.NewGuid(),
                        PromptFile = "prompts/pdf.md",
                        TimeoutSeconds = timeout,
                        MaxPending = 8,
                    },
                },
            };
            validator.Validate(null, ok).Succeeded.ShouldBeTrue();
        }

        foreach (var pending in new[] { 0, 33 })
        {
            var bad = new ChannelOutboundSettings
            {
                Profiles =
                {
                    ["pdf"] = new ChannelOutboundProfileSettings
                    {
                        ProjectId = Guid.NewGuid(),
                        AgentId = Guid.NewGuid(),
                        PromptFile = "prompts/pdf.md",
                        MaxPending = pending,
                    },
                },
            };
            validator.Validate(null, bad).Succeeded.ShouldBeFalse();
        }

        foreach (var pending in new[] { 1, 8, 32 })
        {
            var ok = new ChannelOutboundSettings
            {
                Profiles =
                {
                    ["pdf"] = new ChannelOutboundProfileSettings
                    {
                        ProjectId = Guid.NewGuid(),
                        AgentId = Guid.NewGuid(),
                        PromptFile = "prompts/pdf.md",
                        MaxPending = pending,
                    },
                },
            };
            validator.Validate(null, ok).Succeeded.ShouldBeTrue();
        }

        Enum.GetValues<ChannelOutboundTrigger>().ShouldBe([
            ChannelOutboundTrigger.MarkdownSources,
            ChannelOutboundTrigger.EveryAgentReply,
        ]);
    }
}
