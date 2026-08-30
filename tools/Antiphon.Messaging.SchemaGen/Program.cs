using Antiphon.Messaging.SchemaGen;

var output = args.Length > 0
    ? args[0]
    : Path.Combine(FindRepoRoot(), "docs", "messaging", "contract", "v1");

ContractSchema.Write(output);
Console.WriteLine($"Wrote {ContractSchema.ChannelMessageFileName}, {ContractSchema.ChannelReplyFileName}, and {ContractSchema.InboundUnconsumedEventFileName} to {output}");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            return dir.FullName;
        dir = dir.Parent;
    }

    throw new InvalidOperationException("Could not find Antiphon.sln above " + AppContext.BaseDirectory);
}
