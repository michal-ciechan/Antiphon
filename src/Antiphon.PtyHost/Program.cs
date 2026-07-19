using Antiphon.PtyHost;

// Intermediary mode: re-spawn ourselves fully detached (broken parent chain + job breakaway),
// print the real host's pid on stdout, and exit. The runner always launches hosts through this
// so that killing the runner's process tree (taskkill /T) can never reach a host.
if (args.Length > 0 && args[0] == "--spawn")
{
    var hostArgs = args.Skip(1).ToArray();
    var exePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Cannot determine own executable path for --spawn.");
    var pid = Win32ProcessSpawner.StartDetachedWithFallback(exePath, hostArgs);
    Console.WriteLine(pid);
    return 0;
}

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
