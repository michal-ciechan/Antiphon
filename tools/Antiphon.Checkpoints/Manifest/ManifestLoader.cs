using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Antiphon.Checkpoints;

public static class ManifestLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static CheckpointManifest LoadYaml(string yaml, string worktreeRoot, bool validate = true)
    {
        CheckpointManifest manifest;
        try
        {
            manifest = Deserializer.Deserialize<CheckpointManifest>(yaml) ?? throw new ManifestValidationException("yaml", "empty manifest");
        }
        catch (ManifestValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ManifestValidationException("yaml", "malformed manifest: " + ex.Message);
        }

        manifest.Build ??= new BuildDefaults();
        manifest.Parallel ??= new ParallelDefaults();
        manifest.Timeouts ??= new TimeoutDefaults();
        manifest.Rerun ??= new RerunDefaults();
        manifest.Baseline ??= new BaselineDefaults();
        manifest.Builds ??= [];
        manifest.Checkpoints ??= [];
        manifest.Rerun.KnownFlaky ??= [];
        manifest.Build.Properties ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(manifest.ResultsRoot))
            manifest.ResultsRoot = ".antiphon/checkpoints";
        if (string.IsNullOrWhiteSpace(manifest.Build.Slots))
            manifest.Build.Slots = "auto";
        if (manifest.Build.SlotWaitMinutes <= 0)
            manifest.Build.SlotWaitMinutes = 45;
        if (manifest.Timeouts.RowMinutes <= 0)
            manifest.Timeouts.RowMinutes = 15;
        if (manifest.Timeouts.TotalMinutes <= 0)
            manifest.Timeouts.TotalMinutes = 90;

        if (validate)
            ManifestValidator.Validate(manifest, worktreeRoot);
        return manifest;
    }

    public static CheckpointManifest LoadFile(string path, string worktreeRoot, bool validate = true) =>
        LoadYaml(File.ReadAllText(path), worktreeRoot, validate);

    public static string ToYaml(CheckpointManifest manifest) => Serializer.Serialize(manifest);
}
