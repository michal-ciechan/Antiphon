using System.Text.Json;
using Antiphon.ChannelOutbound.Probe;

// CARD-0418 F-3: parent-owned crash probe.
//
// The parent owns the database, the outbound store and the producer evidence sink, and keeps all
// three between launches. This process does one step of real outbound work against them, announces
// each internal barrier it reaches by writing a marker file, and — when the parent asked it to die
// at a named barrier — parks there forever so the parent can kill it by the PID it recorded.
//
// It loads NO production configuration: everything comes from the generated file named by --config.
if (args is ["--help", ..] || args.Length == 0)
{
    Console.WriteLine("usage: Antiphon.ChannelOutbound.Probe --config <path>");
    return 2;
}

var configPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault();
if (configPath is null || !File.Exists(configPath))
{
    Console.Error.WriteLine("probe: --config <path> is required and must exist");
    return 2;
}

ProbeConfig config;
try
{
    config = JsonSerializer.Deserialize<ProbeConfig>(
        await File.ReadAllTextAsync(configPath), ProbeConfig.JsonOptions)
        ?? throw new InvalidDataException("probe config deserialized to null");
}
catch (Exception ex)
{
    // The configuration carries a connection string. Report the failure, never the file.
    Console.Error.WriteLine("probe: unreadable config (" + ex.GetType().Name + ")");
    return 2;
}

try
{
    return await ProbeRunner.RunAsync(config, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine("probe: " + ex.GetType().Name + ": " + ex.Message);
    return 1;
}
