namespace Antiphon.Server.Application.Settings;

public sealed class WorkspaceAdmissionSettings
{
    public const string SectionName = "WorkspaceAdmission";
    public const int MinimumLockTimeoutMilliseconds = 100;
    public const int MaximumLockTimeoutMilliseconds = 10_000;
    public const int DefaultLockTimeoutMilliseconds = 2_000;

    public int LockTimeoutMilliseconds { get; set; } = DefaultLockTimeoutMilliseconds;
}
