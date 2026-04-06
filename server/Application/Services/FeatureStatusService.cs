using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public class FeatureStatusService(AppDbContext db)
{
    public async Task<FeatureStatusDto> GetFeatureStatusAsync(
        Guid projectId, string featureName, CancellationToken cancellationToken)
    {
        var completedStages = await db.Workflows
            .Where(w => w.ProjectId == projectId
                && w.FeatureName == featureName)
            .SelectMany(w => w.Stages)
            .Where(s => s.Status == StageStatus.Completed)
            .Select(s => s.Name)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new FeatureStatusDto(featureName, completedStages);
    }
}
