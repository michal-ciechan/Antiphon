namespace Antiphon.SessionRunner;

/// <summary>Path resolution only: never probes disk or falls back to the application directory.</summary>
public static class HostLoadStoragePath
{
    public static string Resolve(string? configured, string localData, string userProfile)
    {
        if (configured is not null)
        {
            if (string.IsNullOrWhiteSpace(configured) || !Path.IsPathFullyQualified(configured))
                throw new ArgumentException("host_load_history_path_requires_absolute_directory");
            return Path.GetFullPath(configured);
        }
        if (!string.IsNullOrWhiteSpace(localData) && Path.IsPathFullyQualified(localData))
            return Path.Combine(localData, "antiphon", "host-load");
        if (!string.IsNullOrWhiteSpace(userProfile) && Path.IsPathFullyQualified(userProfile))
            return Path.Combine(userProfile, ".local", "share", "antiphon", "host-load");
        throw new ArgumentException("host_load_history_requires_configured_absolute_directory");
    }
}
