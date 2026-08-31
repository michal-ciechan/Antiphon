using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Server.Infrastructure.Data.Seeding;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-0284: built-in workflow templates must seed current Llm:Providers catalog ids,
/// not dated snapshots (claude-*-20250514 / gpt-4o). YAML modelName is an API model id
/// for AgentExecutor → LlmClientFactory, not a TUI family alias.
/// </summary>
[Category("Integration")]
public sealed class DatabaseSeederTests
{
    private static readonly string[] StaleModelIds =
    [
        "claude-opus-4-20250514",
        "claude-sonnet-4-20250514",
        "gpt-4o",
    ];

    private static readonly Dictionary<string, Dictionary<string, string>> ExpectedYamlModels = new()
    {
        ["Document Project"] = new()
        {
            ["analyze-codebase"] = "gpt-5.2",
            ["finalize-documentation"] = "gpt-5.2",
        },
        ["Full Feature Pipeline"] = new()
        {
            ["prd"] = "claude-opus-4-6",
            ["ux-design"] = "claude-sonnet-4-6",
            ["architecture"] = "claude-opus-4-6",
            ["test-design"] = "claude-opus-4-6",
            ["implementation"] = "claude-opus-4-6",
        },
        ["Quick Change"] = new()
        {
            ["quick-spec"] = "claude-sonnet-4-6",
            ["implement"] = "claude-sonnet-4-6",
            ["code-review"] = "claude-opus-4-6",
        },
    };

    [Test]
    public async Task Seed_writes_current_catalog_model_ids_into_built_in_template_yaml()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));

        await DatabaseSeeder.SeedAsync(db, new LlmSettings(), CancellationToken.None);

        var templates = await db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.IsBuiltIn)
            .ToListAsync();

        templates.Select(t => t.Name).OrderBy(n => n)
            .ShouldBe(["Document Project", "Full Feature Pipeline", "Quick Change"]);

        foreach (var template in templates)
        {
            var stages = WorkflowEngine.ParseYamlDefinition(template.YamlDefinition).Stages;
            var expected = ExpectedYamlModels[template.Name];
            stages.Select(s => s.Name).ShouldBe(expected.Keys, ignoreOrder: true);
            foreach (var stage in stages)
            {
                stage.ModelName.ShouldBe(expected[stage.Name], $"{template.Name}/{stage.Name}");
                StaleModelIds.ShouldNotContain(stage.ModelName);
            }
        }
    }

    [Test]
    public async Task Seed_overwrites_stale_yaml_on_existing_built_in_templates()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));

        db.TemplateGroups.Add(new TemplateGroup
        {
            Id = DatabaseSeeder.BmadGroupId,
            Name = "BMAD",
            Description = "pre-seed",
            IsBuiltIn = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        db.WorkflowTemplates.Add(new WorkflowTemplate
        {
            Id = DatabaseSeeder.BmadFullTemplateId,
            Name = "Full Feature Pipeline",
            Description = "stale",
            YamlDefinition = """
                name: Full Feature Pipeline
                stages:
                  - name: prd
                    executorType: ai-agent
                    modelName: claude-opus-4-20250514
                """,
            IsBuiltIn = true,
            TemplateGroupId = DatabaseSeeder.BmadGroupId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await DatabaseSeeder.SeedAsync(db, new LlmSettings(), CancellationToken.None);

        var yaml = await db.WorkflowTemplates.AsNoTracking()
            .Where(t => t.Id == DatabaseSeeder.BmadFullTemplateId)
            .Select(t => t.YamlDefinition)
            .SingleAsync();
        var stages = WorkflowEngine.ParseYamlDefinition(yaml).Stages;
        stages.Single(s => s.Name == "prd").ModelName.ShouldBe("claude-opus-4-6");
        yaml.ShouldNotContain("claude-opus-4-20250514");
    }

    [Test]
    public async Task Seed_writes_current_catalog_ids_into_default_model_routings()
    {
        await using var isolated = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(isolated.ConnectionString));

        await DatabaseSeeder.SeedAsync(db, new LlmSettings(), CancellationToken.None);

        var routings = await db.ModelRoutings.AsNoTracking().ToListAsync();
        routings.ShouldNotBeEmpty();
        foreach (var routing in routings)
        {
            StaleModelIds.ShouldNotContain(routing.ModelName);
            routing.ModelName.ShouldBeOneOf("claude-opus-4-6", "claude-sonnet-4-6");
        }
    }
}
