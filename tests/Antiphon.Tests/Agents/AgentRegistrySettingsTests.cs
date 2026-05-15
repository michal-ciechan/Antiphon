using Microsoft.Extensions.Configuration;
using Shouldly;
using TUnit.Core;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.Pty;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class AgentRegistrySettingsTests
{
    [Test]
    public void Binds_definitions_kind_exe_and_args_from_configuration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["Agents:DefaultDefinition"] = "claude",
            ["Agents:Definitions:claude:Kind"] = "ClaudeCode",
            ["Agents:Definitions:claude:Exe"] = "cl.bat",
            ["Agents:Definitions:claude:ArgsTemplate:0"] = "--print",
            ["Agents:Definitions:claude:Env:FOO"] = "bar",
            ["Agents:Definitions:raw:Kind"] = "Raw",
            ["Agents:Definitions:raw:Exe"] = "pwsh.exe",
            ["Agents:ClaudeReadyQuietPeriodMs"] = "1234",
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var settings = config.GetSection("Agents").Get<AgentRegistrySettings>()!;

        settings.DefaultDefinition.ShouldBe("claude");
        settings.Definitions.Count.ShouldBe(2);
        settings.Definitions["claude"].Kind.ShouldBe("ClaudeCode");
        settings.Definitions["claude"].Exe.ShouldBe("cl.bat");
        settings.Definitions["claude"].ArgsTemplate.ShouldBe(new[] { "--print" });
        settings.Definitions["claude"].Env["FOO"].ShouldBe("bar");
        settings.Definitions["raw"].Kind.ShouldBe("Raw");
        settings.ClaudeReadyQuietPeriodMs.ShouldBe(1234);
    }

    [Test]
    public void Validator_succeeds_for_well_formed_settings()
    {
        var settings = new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions =
            {
                ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" },
            }
        };

        var result = new AgentRegistrySettingsValidator().Validate(name: null, settings);

        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Validator_rejects_empty_definitions()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "",
            Definitions = new Dictionary<string, AgentDefinition>()
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("at least one entry");
    }

    [Test]
    public void Validator_rejects_definition_with_empty_exe()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Exe must not be empty");
    }

    [Test]
    public void Validator_rejects_unknown_kind()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "weird",
            Definitions = { ["weird"] = new AgentDefinition { Kind = "Wat", Exe = "x" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("not a known AgentKind");
    }

    [Test]
    public void Validator_rejects_default_definition_not_in_dictionary()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "missing",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } }
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("DefaultDefinition 'missing'");
    }

    [Test]
    public void Validator_rejects_non_positive_timing_settings()
    {
        var result = new AgentRegistrySettingsValidator().Validate(name: null, new AgentRegistrySettings
        {
            DefaultDefinition = "claude",
            Definitions = { ["claude"] = new AgentDefinition { Kind = "ClaudeCode", Exe = "cl.bat" } },
            ClaudeReadyQuietPeriodMs = 0,
            ClaudeDoneMaxWaitMs = -5,
        });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ClaudeReadyQuietPeriodMs must be positive");
        result.FailureMessage.ShouldContain("ClaudeDoneMaxWaitMs must be positive");
    }
}
