using Antiphon.Messaging;
using Antiphon.Messaging.Gateway;
using EchoGateway;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Any(a => string.Equals(a, "--self-test", StringComparison.OrdinalIgnoreCase)))
    return await SelfTest.RunAsync();

var builder = Host.CreateApplicationBuilder(args);

// Keep reply text on stdout; host logs go to stderr so the two do not mix.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<IChannelAdapter>(_ => new EchoChannelAdapter(Console.In, Console.Out));
builder.Services.AddAntiphonGateway(builder.Configuration);

Console.Error.WriteLine("EchoGateway channel=echo. Type a line, Enter; replies print here. Ctrl+C to stop.");

await builder.Build().RunAsync();
return 0;
