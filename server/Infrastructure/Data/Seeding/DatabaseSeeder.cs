using Antiphon.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Infrastructure.Data.Seeding;

public static class DatabaseSeeder
{
    public static readonly Guid BmadFullTemplateId = new("b0000000-0000-0000-0000-000000000001");
    public static readonly Guid BmadQuickTemplateId = new("b0000000-0000-0000-0000-000000000002");

    public static async Task SeedAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await SeedWorkflowTemplatesAsync(db, cancellationToken);
    }

    private static async Task SeedWorkflowTemplatesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var existingIds = await db.WorkflowTemplates
            .Where(t => t.IsBuiltIn)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        if (!existingIds.Contains(BmadFullTemplateId))
        {
            db.WorkflowTemplates.Add(new WorkflowTemplate
            {
                Id = BmadFullTemplateId,
                Name = "BMAD Full Workflow",
                Description = "Complete BMAD workflow with all stages: Analyst, PM, Architect, Design, PO, and SM. Includes gates between each stage for review and approval.",
                YamlDefinition = BmadFullYaml,
                IsBuiltIn = true,
                CreatedAt = seedDate,
                UpdatedAt = seedDate
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
                CreatedAt = seedDate,
                UpdatedAt = seedDate
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
