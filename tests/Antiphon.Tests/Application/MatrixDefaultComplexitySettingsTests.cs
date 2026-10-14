using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class MatrixDefaultComplexitySettingsTests
{
    [Test]
    [Arguments(999, "Delegation:DefaultComplexity is 999, which is not a task complexity. Use Hard, Medium or Easy.")]
    [Arguments(-1, "Delegation:DefaultComplexity is -1, which is not a task complexity. Use Hard, Medium or Easy.")]
    [Arguments(3, "Delegation:DefaultComplexity is 3, which is not a task complexity. Use Hard, Medium or Easy.")]
    public void An_undefined_default_complexity_fails_validation(int value, string expected)
    {
        var result = new DelegationSettingsValidator().Validate(null,
            new DelegationSettings { DefaultComplexity = (TaskComplexity)value });

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldNotBeNull().ShouldBe(new[] { expected });
    }

    [Test]
    [Arguments(false, null, TaskComplexity.Medium)]
    [Arguments(true, null, TaskComplexity.Medium)]
    [Arguments(true, "Hard", TaskComplexity.Hard)]
    [Arguments(true, "Medium", TaskComplexity.Medium)]
    [Arguments(true, "Easy", TaskComplexity.Easy)]
    public void The_default_instance_validates(bool present, string? value, TaskComplexity expected)
    {
        var defaults = new DelegationSettings();
        defaults.DefaultComplexity.ShouldBe(TaskComplexity.Medium);
        new DelegationSettingsValidator().Validate(null, defaults).Succeeded.ShouldBeTrue();

        using var services = Bind(present, value);
        services.GetRequiredService<IStartupValidator>().Validate();
        services.GetRequiredService<IOptions<DelegationSettings>>().Value.DefaultComplexity.ShouldBe(expected);
    }

    [Test]
    [Arguments("999", "Delegation:DefaultComplexity is 999, which is not a task complexity. Use Hard, Medium or Easy.")]
    [Arguments("-1", "Delegation:DefaultComplexity is -1, which is not a task complexity. Use Hard, Medium or Easy.")]
    [Arguments("3", "Delegation:DefaultComplexity is 3, which is not a task complexity. Use Hard, Medium or Easy.")]
    public void Startup_rejects_an_undefined_default_complexity(string value, string expected)
    {
        using var services = Bind(true, value);

        var ex = Should.Throw<OptionsValidationException>(() =>
            services.GetRequiredService<IStartupValidator>().Validate());

        ex.Failures.ShouldBe(new[] { expected });
    }

    internal static ServiceProvider Bind(bool present, string? value)
    {
        var values = new Dictionary<string, string?>();
        if (present)
            values["Delegation:DefaultComplexity"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IValidateOptions<DelegationSettings>, DelegationSettingsValidator>();
        services.AddOptions<DelegationSettings>().Bind(configuration.GetSection("Delegation")).ValidateOnStart();
        return services.BuildServiceProvider();
    }
}
