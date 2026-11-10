using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1222: Linux census is a supported omission. Off-Windows methods skip on Windows.
/// A skip is not a pass of the other platform.
/// </summary>
[Category("Unit")]
public sealed class ZombieCensusPlatformTests
{
    private const string OmissionSentence =
        "Zombie census is a supported omission off Windows. Set ZombieCensus:Enabled to false.";

    [Test]
    public void Enabled_true_off_windows_is_rejected()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Windows is not the off-Windows omission path.");
            return;
        }

        var options = new ZombieCensusSettings();
        options.Enabled.ShouldBeTrue();
        var result = new ZombieCensusSettingsValidator().Validate(null, options);
        result.Succeeded.ShouldBeFalse();
        string.Join("\n", result.Failures).ShouldContain(OmissionSentence);
    }

    [Test]
    public void Enabled_false_off_windows_is_accepted()
    {
        var options = new ZombieCensusSettings { Enabled = false };
        options.Enabled.ShouldBeFalse();
        var result = new ZombieCensusSettingsValidator().Validate(null, options);
        result.Succeeded.ShouldBeTrue();
    }

    [Test]
    public void Linux_registration_is_not_the_wmi_census()
    {
        if (OperatingSystem.IsWindows())
        {
            Skip.Test("Windows is not the off-Windows registration path.");
            return;
        }

        var services = new ServiceCollection();
        ZombieCensusRegistration.AddProcessCensus(services);
        using var provider = services.BuildServiceProvider();
        var census = provider.GetRequiredService<IZombieProcessCensus>();
        census.ShouldNotBeOfType<WindowsZombieProcessCensus>();
    }

    [Test]
    public void Bootstrap_records_the_supported_omission()
    {
        var text = DockerStackDocuments.Read("docs/bootstrap.md").Replace("\r\n", "\n");
        var row = text.Split('\n').Single(line => line.Contains("`antiphon:zombie-census`", StringComparison.Ordinal));
        row.ShouldContain(OmissionSentence);
    }

    [Test]
    public void Docker_stack_records_the_supported_omission()
    {
        var text = DockerStackDocuments.Read("docs/docker-stack.md");
        text.ShouldContain(OmissionSentence);
        text.ShouldNotContain("Enabling the flag on Linux throws.");
        text.ShouldNotContain("Census must stay unregistered or the WMI job throws.");
        text.ShouldNotContain("Enabling census on Linux. The job throws.");
        text.ShouldNotContain("Same WMI throw as the compose row.");
    }
}
