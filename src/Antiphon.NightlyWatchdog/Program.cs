using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.NightlyWatchdog;
using Microsoft.Extensions.Logging;

// CARD-0545 entrypoint. Commands: run | --self-check | --reader-login | --send-qualification-notice |
// --stub-windmill <modeFile> | --export-evidence <dir>, each optionally with --config <qual.json>.
// Exit 3 is a refusal (invalid configuration); nothing here names a host.
return await WatchdogProgram.MainAsync(args, Console.In, Console.Out, Console.Error, CancellationToken.None);

namespace Antiphon.NightlyWatchdog
{
    public static class WatchdogProgram
    {
        public sealed record Command(string Name, string? ConfigPath, string? Argument);

        public static Command Parse(IReadOnlyList<string> args)
        {
            string? config = null;
            string? name = null;
            string? argument = null;
            for (var i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "--config" when i + 1 < args.Count: config = args[++i]; break;
                    case "run" or "--self-check" or "--reader-login" or "--send-qualification-notice":
                        name = args[i].TrimStart('-'); break;
                    case "--stub-windmill" or "--export-evidence":
                        name = args[i].TrimStart('-');
                        if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) argument = args[++i];
                        break;
                    default: throw new ArgumentException($"unknown argument '{args[i]}'");
                }
            }
            return new Command(name ?? "run", config, argument);
        }

        public static async Task<int> MainAsync(string[] args, TextReader input, TextWriter output, TextWriter error, CancellationToken ct)
        {
            Command command;
            try { command = Parse(args); }
            catch (ArgumentException ex) { await error.WriteLineAsync(ex.Message); return 2; }

            var options = WatchdogOptions.FromEnvironment(Environment.GetEnvironmentVariable);
            if (command.ConfigPath is { } configPath)
                options.ApplyJson(await File.ReadAllTextAsync(configPath, ct));

            if (command.Name == "reader-login")
                return await TelegramUserReader.LoginInteractiveAsync(options, input, output);
            if (command.Name == "stub-windmill")
                return await WindmillStub.RunAsync(Environment.GetEnvironmentVariable("ANTIPHON_WATCHDOG_STUB_WINDMILL_BIND") ?? "127.0.0.1:17292",
                    command.Argument ?? Path.Combine(options.ResolvedStateDir, "stub-mode"), output, ct);

            var errors = options.Validate();
            if (errors.Count > 0)
            {
                await error.WriteLineAsync("REFUSED: invalid configuration: " + string.Join(", ", errors));
                return 3;
            }

            using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "O "; }));
            var logger = loggerFactory.CreateLogger("watchdog");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds) };
            using var ledger = new Ledger(options.LedgerPath, options.Namespace, TimeProvider.System);

            if (command.Name == "self-check")
                return await SelfCheckAsync(options, ledger, http, output, ct);
            if (command.Name == "export-evidence")
                return await ExportEvidenceAsync(ledger, command.Argument ?? Path.Combine(options.ResolvedStateDir, "evidence"), output);

            await using var reader = new TelegramUserReader(options);
            var loop = new WatchdogLoop(options, ledger, new WindmillHttpApi(http, options),
                new TelegramBotTransport(http, options, () => ledger.Heartbeat().DestinationHash), reader, TimeProvider.System,
                WatchdogLoop.CreateFaultHook(options, point => Environment.FailFast($"qualification crash cut {point}")), logger);

            if (command.Name == "send-qualification-notice")
            {
                var notice = await loop.SendQualificationNoticeAsync(ct);
                await output.WriteLineAsync($"qualification notice nid={notice.Nid} state={notice.State}");
                return 0;
            }

            await using var server = new SnapshotServer(options.SnapshotBind!, () => SnapshotBuilder.Build(ledger, options));
            logger.LogInformation("watchdog {Instance} namespace {Namespace} snapshot on {Bind}", ledger.InstanceId, options.Namespace, server.BoundEndpoint);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Cancel(); });
            await loop.RunAsync(stop.Token);
            return 0;
        }

        private static async Task<int> SelfCheckAsync(WatchdogOptions options, Ledger ledger, HttpClient http, TextWriter output, CancellationToken ct)
        {
            await output.WriteLineAsync($"runtime {RuntimeInformation.FrameworkDescription} {RuntimeInformation.RuntimeIdentifier}");
            await output.WriteLineAsync($"os {RuntimeInformation.OSDescription}");
            if (File.Exists("/etc/os-release"))
            {
                var pretty = (await File.ReadAllLinesAsync("/etc/os-release", ct)).FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal));
                await output.WriteLineAsync($"os-release {pretty}");
            }
            foreach (var day in new[] { new DateOnly(2026, 3, 30), new DateOnly(2026, 10, 25) })
                await output.WriteLineAsync($"due {LondonClock.Format(day)} -> {Ledger.Iso(LondonClock.DueUtc(day))}");
            var probe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antiphon-watchdog-selfcheck-" + Guid.NewGuid().ToString("N") + ".db");
            using (var temp = new Ledger(probe, options.Namespace, TimeProvider.System))
                await output.WriteLineAsync($"sqlite ok ({temp.InstanceId})");
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(probe + suffix);
            await output.WriteLineAsync($"ledger {options.LedgerPath} instance {ledger.InstanceId}");
            await using (var server = new SnapshotServer(options.SnapshotBind!, () => "{}"))
                await output.WriteLineAsync($"snapshot bind ok {server.BoundEndpoint}");
            if (!string.IsNullOrWhiteSpace(options.TelegramBotToken))
            {
                using var response = await http.GetAsync($"{TelegramBotTransport.BaseUrl}/bot{options.TelegramBotToken}/getMe", ct);
                await output.WriteLineAsync($"telegram getMe {(int)response.StatusCode}");
            }
            return 0;
        }

        private static async Task<int> ExportEvidenceAsync(Ledger ledger, string directory, TextWriter output)
        {
            Directory.CreateDirectory(directory);
            var json = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "outages.json"), JsonSerializer.Serialize(ledger.Outages(), json));
            await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "notifications.json"), JsonSerializer.Serialize(ledger.Notifications(), json));
            await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "attempts.json"), JsonSerializer.Serialize(ledger.Attempts(), json));
            await File.WriteAllTextAsync(System.IO.Path.Combine(directory, "receipts.json"), JsonSerializer.Serialize(ledger.Receipts(), json));
            await output.WriteLineAsync($"evidence exported to {directory}");
            return 0;
        }
    }
}
