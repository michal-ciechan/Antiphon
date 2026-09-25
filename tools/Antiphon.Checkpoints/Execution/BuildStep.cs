namespace Antiphon.Checkpoints;

public static class BuildStep
{
    public static IReadOnlyList<string> PropertyArguments(
        IEnumerable<KeyValuePair<string, string>> properties,
        bool isWindows)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in properties)
        {
            if (pair.Key.Equals("OutputPath", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("OutDir", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("BaseOutputPath", StringComparison.OrdinalIgnoreCase))
            {
                throw new ManifestValidationException("msbuildProperty",
                    $"MsBuildProperty '{pair.Key}={pair.Value}' may not set the output path");
            }

            copy[pair.Key] = pair.Value;
        }

        if (!isWindows && !copy.ContainsKey("UseAppHost"))
            copy["UseAppHost"] = "false";

        return copy.Select(pair => "--property:" + pair.Key + "=" + pair.Value).ToList();
    }

    public static List<string> BuildArguments(
        string project,
        string outputPath,
        IReadOnlyList<string> propertyArguments,
        int maxCpuCount)
    {
        var args = new List<string> { "build", project, "--property:OutputPath=" + outputPath };
        args.AddRange(propertyArguments);
        args.Add("-nodeReuse:false");
        if (maxCpuCount > 0)
            args.Add("-maxcpucount:" + maxCpuCount);
        args.Add("--nologo");
        return args;
    }

    public static List<string> RunArguments(
        string project,
        string outputPath,
        IReadOnlyList<string> propertyArguments,
        string filter,
        string resultsDirectory,
        string trxFileName)
    {
        var args = new List<string>
        {
            "run", "--project", project, "--no-build", "--property:OutputPath=" + outputPath,
        };
        args.AddRange(propertyArguments);
        args.Add("--");
        args.Add("--treenode-filter");
        args.Add(filter);
        args.Add("--report-trx");
        args.Add("--report-trx-filename");
        args.Add(trxFileName);
        args.Add("--results-directory");
        args.Add(resultsDirectory);
        return args;
    }
}
