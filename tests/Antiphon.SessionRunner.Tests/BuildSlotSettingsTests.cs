using Antiphon.SessionRunner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>CARD-0589 V-3 (S2): <c>SessionRunner:BuildSlots</c> binds and refuses a budget that cannot grant.</summary>
[Category("Unit")]
public sealed class BuildSlotSettingsTests
{
    [Test]
    public void Validation_rejects_a_zero_budget_cpu_count_or_ttl()
    {
        foreach (var (name, broken) in new (string, BuildSlotSettings)[]
                 {
                     ("MaxConcurrent", new BuildSlotSettings { MaxConcurrent = 0 }),
                     ("MaxCpuCount", new BuildSlotSettings { MaxCpuCount = 0 }),
                     ("LeaseTtlMinutes", new BuildSlotSettings { LeaseTtlMinutes = 0 }),
                 })
        {
            var ex = Should.Throw<InvalidOperationException>(() => broken.Validate(), name);
            ex.Message.ShouldContain("SessionRunner:BuildSlots:" + name);
        }

        Should.NotThrow(() => new BuildSlotSettings().Validate());

        // Through the runner's own registration a broken value fails when options are first resolved.
        using var provider = Provider(new() { ["SessionRunner:BuildSlots:MaxConcurrent"] = "0" });
        Should.Throw<InvalidOperationException>(() => _ = provider.GetRequiredService<IOptions<BuildSlotSettings>>().Value);
    }

    [Test]
    public void Settings_bind_from_the_SessionRunner_BuildSlots_section_over_the_desktop_defaults()
    {
        using var defaults = Provider(new());
        var d = defaults.GetRequiredService<IOptions<BuildSlotSettings>>().Value;
        (d.Enabled, d.MaxConcurrent, d.MaxCpuCount, d.MinAvailableMemoryMb, d.LeaseTtlMinutes, d.RetryAfterMs, d.WaiterSilenceMs, d.SweepIntervalMs)
            .ShouldBe((true, 2, 4, 6144, 90, 15_000, 60_000, 30_000));

        using var server2 = Provider(new()
        {
            ["SessionRunner:BuildSlots:MaxConcurrent"] = "4",
            ["SessionRunner:BuildSlots:MaxCpuCount"] = "6",
            ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = "16384",
            ["SessionRunner:BuildSlots:Enabled"] = "false",
        });
        var s = server2.GetRequiredService<IOptions<BuildSlotSettings>>().Value;
        (s.MaxConcurrent, s.MaxCpuCount, s.MinAvailableMemoryMb, s.Enabled).ShouldBe((4, 6, 16384, false));
        server2.GetRequiredService<BuildSlotBroker>().List().Budget.ShouldBe(4);
    }

    private static ServiceProvider Provider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBuildSlotBroker(configuration);
        return services.BuildServiceProvider();
    }
}
