using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Tests.TestHelpers;

internal static class SeededWorkflowTemplates
{
    public static async Task EnsureFullFeaturePipelineAsync(AppDbContext db)
    {
        var id = AgentPresets.FullFeaturePipelineTemplateId;
        if (await db.WorkflowTemplates.AnyAsync(t => t.Id == id))
            return;

        db.WorkflowTemplates.Add(new WorkflowTemplate
        {
            Id = id,
            Name = "Full Feature Pipeline",
            Description = "CARD-0255 test pin",
            YamlDefinition = "name: Full Feature Pipeline\nstages: []",
            IsBuiltIn = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            foreach (var entry in db.ChangeTracker.Entries<WorkflowTemplate>()
                         .Where(e => e.Entity.Id == id)
                         .ToList())
                entry.State = EntityState.Detached;
            if (!await db.WorkflowTemplates.AnyAsync(t => t.Id == id))
                throw;
        }
    }
}
