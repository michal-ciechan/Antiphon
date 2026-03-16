using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data.Seeding;

public static class DatabaseSeeder
{
    public static readonly Guid BmadFullTemplateId = new("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid BmadQuickTemplateId = new("b0000000-0000-0000-0000-000000000002");

    public static readonly Guid DefaultAdminId = new("a0000000-0000-0000-0000-000000000001");

    public static readonly Guid AnthropicProviderId = new("c0000000-0000-0000-0000-000000000001");
    public static readonly Guid OpenAiProviderId = new("c0000000-0000-0000-0000-000000000002");
    public static readonly Guid OllamaProviderId = new("c0000000-0000-0000-0000-000000000003");

    public static readonly Guid RoutingBusinessAnalysisId = new("d0000000-0000-0000-0000-000000000001");
    public static readonly Guid RoutingProductManagementId = new("d0000000-0000-0000-0000-000000000002");
    public static readonly Guid RoutingArchitectureId = new("d0000000-0000-0000-0000-000000000003");
    public static readonly Guid RoutingUxDesignId = new("d0000000-0000-0000-0000-000000000004");
    public static readonly Guid RoutingProductOwnerReviewId = new("d0000000-0000-0000-0000-000000000005");
    public static readonly Guid RoutingScrumMasterPlanningId = new("d0000000-0000-0000-0000-000000000006");
    public static readonly Guid RoutingRequirementsAnalysisId = new("d0000000-0000-0000-0000-000000000007");
    public static readonly Guid RoutingArchitectureAndDesignId = new("d0000000-0000-0000-0000-000000000008");
    public static readonly Guid RoutingImplementationPlanningId = new("d0000000-0000-0000-0000-000000000009");

    private static readonly DateTime SeedDate = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await SeedDefaultAdminAsync(db, cancellationToken);
        await SeedWorkflowTemplatesAsync(db, cancellationToken);
        await SeedDefaultProvidersAsync(db, cancellationToken);
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

    private static async Task SeedDefaultModelRoutingAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingStageNames = await db.ModelRoutings
            .Select(r => r.StageName)
            .ToListAsync(cancellationToken);

        // Map stage names from BMAD workflows to models via Anthropic provider.
        // Opus for high-complexity stages, Sonnet for standard stages.
        var routings = new (Guid Id, string StageName, string ModelName)[]
        {
            (RoutingBusinessAnalysisId, "business-analysis", "claude-opus-4-20250514"),
            (RoutingProductManagementId, "product-management", "claude-opus-4-20250514"),
            (RoutingArchitectureId, "architecture", "claude-opus-4-20250514"),
            (RoutingUxDesignId, "ux-design", "claude-sonnet-4-20250514"),
            (RoutingProductOwnerReviewId, "product-owner-review", "claude-opus-4-20250514"),
            (RoutingScrumMasterPlanningId, "scrum-master-planning", "claude-sonnet-4-20250514"),
            (RoutingRequirementsAnalysisId, "requirements-analysis", "claude-sonnet-4-20250514"),
            (RoutingArchitectureAndDesignId, "architecture-and-design", "claude-opus-4-20250514"),
            (RoutingImplementationPlanningId, "implementation-planning", "claude-sonnet-4-20250514"),
        };

        foreach (var (id, stageName, modelName) in routings)
        {
            if (!existingStageNames.Contains(stageName))
            {
                db.ModelRoutings.Add(new ModelRouting
                {
                    Id = id,
                    StageName = stageName,
                    ModelName = modelName,
                    ProviderId = AnthropicProviderId,
                    CreatedAt = SeedDate
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedWorkflowTemplatesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingIds = await db.WorkflowTemplates
            .Where(t => t.IsBuiltIn)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (!existingIds.Contains(BmadFullTemplateId))
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadFullTemplateId,
                Name = "BMAD Full Workflow",
                Description = "Complete BMAD workflow with all stages: Analyst, PM, Architect, Design, PO, and SM. Includes gates between each stage for review and approval.",
                YamlDefinition = BmadFullYaml,
                IsBuiltIn = true,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }

        if (!existingIds.Contains(BmadQuickTemplateId))
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadQuickTemplateId,
                Name = "BMAD Quick Workflow",
                Description = "Streamlined BMAD workflow with condensed stages for rapid prototyping. Combines analysis and architecture into fewer stages with simplified gates.",
                YamlDefinition = BmadQuickYaml,
                IsBuiltIn = true,
                CreatedAt = SeedDate,
                UpdatedAt = SeedDate
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private const string BmadFullYaml = """
        name: BMAD Full Workflow
        description: Complete BMAD workflow with all stages
        stages:
          - name: business-analysis
            executorType: ai-agent
            modelRouting: opus
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Analyze the project requirements and produce a comprehensive
              Business Requirements Document (BRD) covering stakeholders,
              user stories, acceptance criteria, and success metrics.
          - name: product-management
            executorType: ai-agent
            modelRouting: opus
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Create a Product Requirements Document (PRD) based on the BRD.
              Define functional requirements, non-functional requirements,
              UX design requirements, and prioritization.
          - name: architecture
            executorType: ai-agent
            modelRouting: opus
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Design the system architecture based on the PRD.
              Produce an Architecture Decision Document covering tech stack,
              component design, data model, API design, and deployment strategy.
          - name: ux-design
            executorType: ai-agent
            modelRouting: sonnet
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Create a UX Design Specification based on the PRD and architecture.
              Define wireframes, interaction patterns, component library choices,
              and responsive design strategy.
          - name: product-owner-review
            executorType: ai-agent
            modelRouting: opus
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Review all upstream artifacts for completeness and consistency.
              Produce an epic breakdown with stories, acceptance criteria,
              and dependency mapping.
          - name: scrum-master-planning
            executorType: ai-agent
            modelRouting: sonnet
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Create implementation plan with sprint breakdown,
              story sequencing, risk assessment, and team capacity planning.
        """;

    private const string BmadQuickYaml = """
        name: BMAD Quick Workflow
        description: Streamlined BMAD workflow for rapid prototyping
        stages:
          - name: requirements-analysis
            executorType: ai-agent
            modelRouting: sonnet
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Perform combined business and product analysis.
              Produce a lightweight requirements document covering
              key user stories, core features, and technical constraints.
          - name: architecture-and-design
            executorType: ai-agent
            modelRouting: opus
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Design both system architecture and UX in a single pass.
              Focus on key architectural decisions and core UI patterns.
              Produce a combined architecture and design document.
          - name: implementation-planning
            executorType: ai-agent
            modelRouting: sonnet
            gateConfig:
              enabled: true
              approvalRequired: true
            instructions: >
              Create an implementation plan with epic breakdown,
              story list, and prioritized backlog ready for development.
        """;
}
