using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Tests.Application;

internal static class InternalDecisionFixtures
{
    public const string Preserve =
        "Restore the existing production-backup command's intended arguments. Keep backup target, data, credentials, retention and deployment guards unchanged. Local edits only.";

    public static InternalDecisionPolicyRequest Sample(
        string id = "backup-transport",
        IReadOnlyList<InternalDecisionCategory>? categories = null,
        IReadOnlyList<string>? paths = null,
        IReadOnlyList<string>? attributeTargets = null,
        string? preserve = null)
    {
        var resolvedPaths = paths ?? ["scripts/deploy-gym-stat.ps1", ".gitattributes"];
        var resolvedTargets = attributeTargets;
        if (paths is null && attributeTargets is null)
            resolvedTargets = ["scripts/deploy-gym-stat.ps1"];

        return new(
            Version: 1,
            Grants:
            [
                new InternalDecisionGrantRequest(
                    Id: id,
                    Categories: categories ??
                    [
                        InternalDecisionCategory.LineEndings,
                        InternalDecisionCategory.ShellTransport,
                    ],
                    Paths: resolvedPaths,
                    AttributeTargets: resolvedTargets,
                    Preserve: preserve ?? Preserve),
            ]);
    }

    public static StoredInternalDecisionGrantedBy ManualGrantor() =>
        new("manual", null, null, null, null);

    public static DateTime GrantedAt { get; } = new(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc);
}
