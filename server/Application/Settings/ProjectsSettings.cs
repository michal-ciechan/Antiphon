namespace Antiphon.Server.Application.Settings;

/// <summary>Settings for project-list projections that otherwise inspect the local filesystem.</summary>
public sealed class ProjectsSettings
{
    public int ReadinessCacheSeconds { get; set; } = 60;
}
