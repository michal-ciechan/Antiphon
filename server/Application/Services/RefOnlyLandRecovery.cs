using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

internal static class RefOnlyLandRecovery
{
    public static bool IsUnsupported(AgentTaskLandRequest request) =>
        request.SourceAdvanceChildOperation == "source-adopt-reset" || request.RecoveryAdoptedAt is not null
            ? true
            : request.RecoveryMode switch
            {
                LandRecoveryMode.OwnerReviewedSource => true,
                LandRecoveryMode.AdoptReviewedSource => true,
                LandRecoveryMode.None => false,
                _ => true,
            };
}
