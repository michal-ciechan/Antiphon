using Antiphon.PtyHost;

var options = PtyHostOptions.Parse(args);
var log = new HostLog(options.LogFile);

log.Info($"pty-host starting: session {options.SessionId}, pipe {options.PipeName}, pid {Environment.ProcessId}");

await using var session = new HostSession(options, log);
var server = new PtyHostServer(options, session, log);

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

await server.RunAsync(shutdown.Token);
return 0;
