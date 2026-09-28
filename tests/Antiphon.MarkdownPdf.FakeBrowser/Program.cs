using System.Diagnostics;

var root = Environment.GetEnvironmentVariable("ANTIPHON_CARD0418_BROWSER_ROOT")
    ?? throw new InvalidOperationException("Missing fixture root.");
Directory.CreateDirectory(root);
if (args.Contains("--fixture-child", StringComparer.Ordinal))
{
    await WritePidAsync("child.pid");
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
var start = new ProcessStartInfo(executable) { UseShellExecute = false };
if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
start.ArgumentList.Add("--fixture-child");
using var child = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
await WritePidAsync("parent.pid");
await Task.Delay(Timeout.InfiniteTimeSpan);

async Task WritePidAsync(string name)
{
    var target = Path.Combine(root, name);
    var temporary = target + ".tmp";
    await File.WriteAllTextAsync(temporary,
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    File.Move(temporary, target);
}
