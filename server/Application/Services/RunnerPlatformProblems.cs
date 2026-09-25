namespace Antiphon.Server.Application.Services;

/// <summary>CARD-0710 problem codes. Safe detail only: platform, runner id, observed platform.</summary>
public static class RunnerPlatformProblems
{
    public const string Mismatch = "runner_platform_mismatch";
    public const string Unknown = "runner_platform_unknown";
    public const string Unavailable = "runner_platform_unavailable";
    public const string Conflict = "runner_platform_conflict";
    public const string EnforcementUnsupported = "runner_platform_enforcement_unsupported";
    public const string RequirementInvalid = "runner_platform_requirement_invalid";
    public const string DefaultsRevision = "runner_defaults_revision_conflict";
    public const string DefaultsHuman = "runner_defaults_human";
}
