using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public sealed class MatrixDefaultComplexitySettingsTests
{
    [Test]
    [Category("Unit")]
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
    [Category("Unit")]
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
    [Category("Unit")]
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

    // S-0 compatibility boundary. S-A intentionally changes the seeded-row case.
    [Test]
    [Category("Integration")]
    [Arguments(false, null)]
    [Arguments(true, null)]
    [Arguments(true, "Medium")]
    public async Task The_default_setting_keeps_a_plain_create_on_role_policy(bool present, string? value)
    {
        using var services = Bind(present, value);
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var workspace = Directory.CreateTempSubdirectory("antiphon-matrix-default-settings");
        try
        {
            settings.AllowedRoots = [workspace.FullName];
            db.ComplexityChains.Add(new ComplexityChain
            {
                Id = Guid.NewGuid(),
                Role = AgentTaskRole.Plan,
                Complexity = TaskComplexity.Medium,
                CandidatesJson = ComplexityChain.SerializeCandidates([new(AgentKind.Codex, AgentModelLevel.Low)]),
                Provenance = RoutingPinProvenance.Human,
                Reason = "S-0 keeps the role policy despite a disagreeing default-tier cell",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            var options = Options.Create(settings);
            var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
            var routing = new ComplexityRoutingService(db, options, TimeProvider.System, availability);
            var service = new AgentTaskService(
                db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                options, new MockEventBus(), new RecordingSessionStopper(), TimeProvider.System,
                NullLogger<AgentTaskService>.Instance,
                modelAvailability: availability,
                routingPins: new RoutingPinService(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance),
                complexityRouting: routing);

            var created = await service.CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared),
                new AgentTaskService.Caller(null, null, workspace.FullName), CancellationToken.None);

            created.Status.ShouldBe(AgentTaskStatus.Queued);
            created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
            created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
            created.Complexity.ShouldBeNull();
            created.Routing.ShouldBeNull();
            db.ChangeTracker.Clear();
            var saved = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
            saved.Status.ShouldBe(AgentTaskStatus.Queued);
            saved.AgentKind.ShouldBe(AgentKind.ClaudeCode);
            saved.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
            saved.Complexity.ShouldBeNull();
            saved.RoutingPinId.ShouldBeNull();
            var createdEvent = await db.AgentTaskEvents.SingleAsync(
                e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
            createdEvent.Detail.ShouldContain("(role policy)");
        }
        finally
        {
            workspace.Delete(recursive: true);
        }
    }

    private static ServiceProvider Bind(bool present, string? value)
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
