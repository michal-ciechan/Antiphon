using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data.Seeding;

public static class DatabaseSeeder
{
    public static readonly Guid BmadGroupId = new("e0000000-0000-0000-0000-000000000001");

    public static readonly Guid BmadFullTemplateId = new("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid BmadQuickTemplateId = new("b0000000-0000-0000-0000-000000000002");
    public static readonly Guid BmadDocProjectTemplateId = new("b0000000-0000-0000-0000-000000000003");

    public static readonly Guid DefaultAdminId = new("a0000000-0000-0000-0000-000000000001");

    public static readonly Guid AnthropicProviderId = new("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid OpenAiProviderId = new("c0000000-0000-0000-0000-000000000002");
    public static readonly Guid OllamaProviderId = new("c0000000-0000-0000-0000-000000000003");

    // Document Project routings
    public static readonly Guid RoutingDocAnalyzeId = new("d0000000-0000-0000-0000-000000000001");
    public static readonly Guid RoutingDocFinalizeId = new("d0000000-0000-0000-0000-000000000002");
    // Full Feature Pipeline routings
    public static readonly Guid RoutingFullPrdId = new("d0000000-0000-0000-0000-000000000003");
    public static readonly Guid RoutingFullUxDesignId = new("d0000000-0000-0000-0000-000000000004");
    public static readonly Guid RoutingFullArchitectureId = new("d0000000-0000-0000-0000-000000000005");
    public static readonly Guid RoutingFullTestDesignId = new("d0000000-0000-0000-0000-000000000006");
    public static readonly Guid RoutingFullImplementationId = new("d0000000-0000-0000-0000-000000000007");
    // Quick Change routings
    public static readonly Guid RoutingQuickSpecId = new("d0000000-0000-0000-0000-000000000008");
    public static readonly Guid RoutingQuickImplementId = new("d0000000-0000-0000-0000-000000000009");
    public static readonly Guid RoutingQuickCodeReviewId = new("d0000000-0000-0000-0000-000000000010");

    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(AppDbContext db, LlmSettings llmSettings, CancellationToken cancellationToken)
    {
        await SeedDefaultAdminAsync(db, cancellationToken);
        await SeedTemplateGroupsAsync(db, cancellationToken);
        await SeedWorkflowTemplatesAsync(db, cancellationToken);
        await SeedDefaultProvidersAsync(db, cancellationToken);
        await SyncProviderConfigAsync(db, llmSettings, cancellationToken);
        await SeedDefaultModelRoutingAsync(db, cancellationToken);
    }

    private static async Task SeedDefaultAdminAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == DefaultAdminId, cancellationToken);
        if (exists)
            return;

        db.Users.Add(new User
        {
            Id = DefaultAdminId,
            UserName = "admin",
            Email = "admin@antiphon.local",
            IsAdmin = true,
            CreatedAt = SeedDate
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedTemplateGroupsAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var exists = await db.TemplateGroups.AnyAsync(g => g.Id == BmadGroupId, cancellationToken);
        if (!exists)
        {
            db.TemplateGroups.Add(new TemplateGroup
            {
                Id = BmadGroupId,
                Name = "BMAD",
                Description = "BMad Method — AI-assisted development framework with structured workflows for the full software development lifecycle.",
                IsBuiltIn = true,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedWorkflowTemplatesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        // Document Project — insert or update
        var docTemplate = await db.WorkflowTemplates.FirstOrDefaultAsync(
            t => t.Id == BmadDocProjectTemplateId, cancellationToken);
        if (docTemplate is null)
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadDocProjectTemplateId,
                Name = "Document Project",
                Description = "Analyze codebase and generate project documentation",
                YamlDefinition = DocProjectYaml,
                IsBuiltIn = true,
                TemplateGroupId = BmadGroupId,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }
        else
        {
            docTemplate.Name = "Document Project";
            docTemplate.Description = "Analyze codebase and generate project documentation";
            docTemplate.YamlDefinition = DocProjectYaml;
            docTemplate.TemplateGroupId = BmadGroupId;
            docTemplate.UpdatedAt = SeedDate;
        }

        // Full Feature Pipeline — insert or update
        var fullTemplate = await db.WorkflowTemplates.FirstOrDefaultAsync(
            t => t.Id == BmadFullTemplateId, cancellationToken);
        if (fullTemplate is null)
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadFullTemplateId,
                Name = "Full Feature Pipeline",
                Description = "Complete BMAD pipeline for significant features. Select the stages you need.",
                YamlDefinition = BmadFullYaml,
                IsBuiltIn = true,
                TemplateGroupId = BmadGroupId,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }
        else
        {
            fullTemplate.Name = "Full Feature Pipeline";
            fullTemplate.Description = "Complete BMAD pipeline for significant features. Select the stages you need.";
            fullTemplate.YamlDefinition = BmadFullYaml;
            fullTemplate.TemplateGroupId = BmadGroupId;
            fullTemplate.UpdatedAt = SeedDate;
        }

        // Quick Change — insert or update
        var quickTemplate = await db.WorkflowTemplates.FirstOrDefaultAsync(
            t => t.Id == BmadQuickTemplateId, cancellationToken);
        if (quickTemplate is null)
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadQuickTemplateId,
                Name = "Quick Change",
                Description = "Lightweight spec and implementation for small, self-contained changes.",
                YamlDefinition = BmadQuickYaml,
                IsBuiltIn = true,
                TemplateGroupId = BmadGroupId,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }
        else
        {
            quickTemplate.Name = "Quick Change";
            quickTemplate.Description = "Lightweight spec and implementation for small, self-contained changes.";
            quickTemplate.YamlDefinition = BmadQuickYaml;
            quickTemplate.TemplateGroupId = BmadGroupId;
            quickTemplate.UpdatedAt = SeedDate;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedDefaultProvidersAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingIds = await db.LlmProviders
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        if (!existingIds.Contains(AnthropicProviderId))
        {
            db.LlmProviders.Add(new LlmProvider
            {
                Id = AnthropicProviderId,
                Name = "Anthropic",
                ProviderType = ProviderType.Anthropic,
                ApiKey = string.Empty,
                BaseUrl = "https://api.anthropic.com",
                IsEnabled = false,
                DefaultModel = "claude-sonnet-4-20250514",
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }

        if (!existingIds.Contains(OpenAiProviderId))
        {
            db.LlmProviders.Add(new LlmProvider
            {
                Id = OpenAiProviderId,
                Name = "OpenAI",
                ProviderType = ProviderType.OpenAI,
                ApiKey = string.Empty,
                BaseUrl = "https://api.openai.com",
                IsEnabled = false,
                DefaultModel = "gpt-4o",
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }

        if (!existingIds.Contains(OllamaProviderId))
        {
            db.LlmProviders.Add(new LlmProvider
            {
                Id = OllamaProviderId,
                Name = "Ollama",
                ProviderType = ProviderType.Ollama,
                ApiKey = string.Empty,
                BaseUrl = "http://localhost:11434",
                IsEnabled = false,
                DefaultModel = "llama3",
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Syncs BaseUrl and ApiKey from LlmSettings config (including user secrets overrides)
    /// into the DB provider records. Matches by provider name (case-insensitive).
    /// This ensures local user-secret overrides are reflected in the Settings UI.
    /// </summary>
    private static async Task SyncProviderConfigAsync(
        AppDbContext db, LlmSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Providers.Count == 0)
            return;

        var providers = await db.LlmProviders.ToListAsync(cancellationToken);
        var changed = false;

        foreach (var (providerName, config) in settings.Providers)
        {
            var dbProvider = providers.FirstOrDefault(
                p => p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));

            if (dbProvider is null)
                continue;

            if (!string.IsNullOrEmpty(config.BaseUrl) && dbProvider.BaseUrl != config.BaseUrl)
            {
                dbProvider.BaseUrl = config.BaseUrl;
                dbProvider.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }

            if (!string.IsNullOrEmpty(config.ApiKey) && dbProvider.ApiKey != config.ApiKey)
            {
                dbProvider.ApiKey = config.ApiKey;
                dbProvider.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        if (changed)
            await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedDefaultModelRoutingAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingIds = await db.ModelRoutings
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        var routings = new (Guid Id, string StageName, string ModelName, Guid TemplateId)[]
        {
            // Document Project
            (RoutingDocAnalyzeId, "analyze-codebase", "claude-opus-4-20250514", BmadDocProjectTemplateId),
            (RoutingDocFinalizeId, "finalize-documentation", "claude-sonnet-4-20250514", BmadDocProjectTemplateId),
            // Full Feature Pipeline
            (RoutingFullPrdId, "prd", "claude-opus-4-20250514", BmadFullTemplateId),
            (RoutingFullUxDesignId, "ux-design", "claude-sonnet-4-20250514", BmadFullTemplateId),
            (RoutingFullArchitectureId, "architecture", "claude-opus-4-20250514", BmadFullTemplateId),
            (RoutingFullTestDesignId, "test-design", "claude-opus-4-20250514", BmadFullTemplateId),
            (RoutingFullImplementationId, "implementation", "claude-opus-4-20250514", BmadFullTemplateId),
            // Quick Change
            (RoutingQuickSpecId, "quick-spec", "claude-sonnet-4-20250514", BmadQuickTemplateId),
            (RoutingQuickImplementId, "implement", "claude-sonnet-4-20250514", BmadQuickTemplateId),
            (RoutingQuickCodeReviewId, "code-review", "claude-opus-4-20250514", BmadQuickTemplateId),
        };

        foreach (var (id, stageName, modelName, templateId) in routings)
        {
            if (!existingIds.Contains(id))
            {
                db.ModelRoutings.Add(new ModelRouting
                {
                    Id = id,
                    StageName = stageName,
                    ModelName = modelName,
                    ProviderId = AnthropicProviderId,
                    WorkflowTemplateId = templateId,
                    CreatedAt = SeedDate
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private const string DocProjectYaml = """
        name: Document Project
        description: Analyze codebase and generate project documentation
        stages:
          - name: analyze-codebase
            executorType: ai-agent
            modelName: gpt-4o
            gateRequired: true
            systemPrompt: |
              Analyze the existing codebase thoroughly. Document architectural patterns,
              naming conventions, runtime constraints, domain concepts, and infrastructure.
              Only document what demonstrably exists -- no extrapolation.
              Surface uncertainties in a REVIEWER_TODO section at the end.

              Use party mode: analyze the codebase from multiple perspectives simultaneously —
              as a new developer onboarding, as an architect reviewing design decisions, and as
              a security reviewer checking for concerns. Synthesize these viewpoints into a
              comprehensive, well-structured document.
          - name: finalize-documentation
            executorType: ai-agent
            modelName: gpt-4o
            gateRequired: false
            systemPrompt: |
              Incorporate the review feedback into the project documentation.
              Move resolved questions to ADR files. Finalize and commit the documentation.
              Use party mode: have a technical writer, an architect, and a developer each
              review the documentation for clarity, completeness, and accuracy.
        """;

    private const string BmadFullYaml = """
        name: Full Feature Pipeline
        description: Complete BMAD pipeline for significant features. Select the stages you need.
        selectableStages: true
        stages:
          - name: prd
            executorType: ai-agent
            modelName: claude-opus-4-20250514
            gateRequired: true
            systemPrompt: |
              Create a combined Product Brief and Product Requirements Document (PRD).
              Establish the vision, goals, and scope for this feature or initiative.
              Define functional requirements, non-functional requirements, user stories,
              acceptance criteria, and success metrics.
              Surface any uncertainties or open questions in a REVIEWER_TODO section.
          - name: ux-design
            executorType: ai-agent
            modelName: claude-sonnet-4-20250514
            gateRequired: true
            systemPrompt: |
              Create UX design specifications for this feature.
              Generate example HTML files showing 3-5 design options per key screen.
              Document component selections, page layouts, and interaction patterns.
              Include responsive design strategy and accessibility considerations.
          - name: architecture
            executorType: ai-agent
            modelName: claude-opus-4-20250514
            gateRequired: true
            systemPrompt: |
              Design the technical architecture and solution design for this feature.
              Cover technology choices, component design, data model changes, API design,
              communication patterns, infrastructure decisions, and deployment strategy.
              Document key architectural decisions and trade-offs.
          - name: test-design
            executorType: ai-agent
            modelName: claude-opus-4-20250514
            gateRequired: true
            systemPrompt: |
              Create system-level and feature-level test plans.
              Define test scope, coverage strategy, test categories (unit, integration, e2e),
              and acceptance criteria. Consider both automated and manual testing approaches.
          - name: implementation
            executorType: ai-agent
            modelName: claude-opus-4-20250514
            gateRequired: true
            systemPrompt: |
              Break the requirements into epics and user stories with clear acceptance criteria.
              For each story: implement the code following project conventions, write tests,
              verify the implementation works, and commit.
              Loop through all epics and stories until every story is complete.
              Do not stop or give up -- try different approaches if something fails.
              Use party mode (multi-perspective analysis) for complex decisions.
        """;

    private const string BmadQuickYaml = """
        name: Quick Change
        description: Lightweight spec and implementation for small, self-contained changes.
        stages:
          - name: quick-spec
            executorType: ai-agent
            modelName: claude-sonnet-4-20250514
            gateRequired: true
            systemPrompt: |
              Create a lightweight technical specification for this change.
              Cover what needs to change, why, how it will be implemented,
              acceptance criteria, and any technical considerations or risks.
              Keep it concise -- this is for small, well-understood changes.
          - name: implement
            executorType: ai-agent
            modelName: claude-sonnet-4-20250514
            gateRequired: false
            systemPrompt: |
              Implement the change described in the quick spec.
              Write clean code following project conventions.
              Include appropriate tests. Verify the implementation works.
              Do not stop or give up -- try different approaches if needed.
          - name: code-review
            executorType: ai-agent
            modelName: claude-opus-4-20250514
            gateRequired: true
            systemPrompt: |
              Perform an adversarial code review of the implementation.
              Check for correctness, edge cases, security issues, performance concerns,
              and adherence to project conventions. Auto-implement accepted recommendations.
              Document decisions with confidence levels (high/medium/low).
        """;
}
