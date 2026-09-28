using System.Diagnostics;

var root = Environment.GetEnvironmentVariable("ANTIPHON_CARD0418_BROWSER_ROOT")
    ?? throw new InvalidOperationException("Missing fixture root.");
Directory.CreateDirectory(root);
if (args.Contains("--fixture-child", StringComparer.Ordinal))
{
    await File.WriteAllTextAsync(Path.Combine(root, "child.pid"),
        Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return;
}

var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Missing process path.");
var start = new ProcessStartInfo(executable) { UseShellExecute = false };
if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
    start.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
start.ArgumentList.Add("--fixture-child");
using var child = Process.Start(start) ?? throw new InvalidOperationException("Child did not start.");
await File.WriteAllTextAsync(Path.Combine(root, "parent.pid"),
    Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
await Task.Delay(Timeout.InfiniteTimeSpan);
