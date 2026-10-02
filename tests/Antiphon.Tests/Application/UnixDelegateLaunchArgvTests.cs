using System.Diagnostics;
using System.Text;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class UnixDelegateLaunchArgvTests
{
    [Test]
    [Arguments(AgentTaskRole.Investigate)]
    [Arguments(AgentTaskRole.Code)]
    public async Task Composed_delegate_argv_reaches_native_child(AgentTaskRole role)
    {
        if (OperatingSystem.IsWindows()) throw new InvalidOperationException("Unix checkpoint selected on Windows");
        var root = Path.Combine(Path.GetTempPath(), "c863-compose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var capture = Path.Combine(root, "argv.bin");
        var child = Path.Combine(root, "owned-argv-child.sh");
        File.WriteAllText(child, "#!/bin/sh\ntemp=\"$ANTIPHON_TEST_ARGV.tmp.$$\"\nprintf '%s\\0' \"$@\" > \"$temp\"\nmv \"$temp\" \"$ANTIPHON_TEST_ARGV\"\nprintf 'ARGV_CAPTURED\\n'\nwhile :; do sleep 1; done\n");
        File.SetUnixFileMode(child, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var (dispatcher, provider) = GrokRulesLaunchRefusalTests.CreateDelegateHarness();
        using var ownedProvider = provider;
        var task = GrokRulesLaunchRefusalTests.TaskFor(AgentTaskKind.Worker, role);
        var sessionId = Guid.NewGuid();
        var spec = GrokRulesLaunchRefusalTests.SpecOf(dispatcher, task, AgentKind.ClaudeCode, null);
        var withIdentity = AgentSessionService.BuildSessionIdentityArgs(spec.Args, sessionId, resumeMode: null);
        var argv = ClaudeRemoteControlLaunchArgs.ApplyOff(AgentKind.ClaudeCode, withIdentity).ToArray();
        var append = Array.IndexOf(argv, "--append-system-prompt");
        append.ShouldBeGreaterThanOrEqualTo(0, "composed-append-present");
        argv[append + 1].Length.ShouldBeGreaterThan(1024, "composed-append-exceeds-1024");
        argv[append + 1].ShouldContain("[bundle:");
        var identity = Array.IndexOf(argv, "--session-id");
        identity.ShouldBeGreaterThan(append, "composed-identity-position");
        argv[identity + 1].ShouldBe(sessionId.ToString("D"));
        var expected = Encoding.UTF8.GetBytes(string.Concat(argv.Select(arg => arg + "\0")));
        await using var runner = new PtyAgentRunner("inbox");
        try
        {
            await runner.StartAsync(child, argv, root,
                new Dictionary<string, string> { ["ANTIPHON_TEST_ARGV"] = capture });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(capture))
            {
                deadline.Token.ThrowIfCancellationRequested();
                await Task.Delay(20, deadline.Token);
            }
            var actual = await File.ReadAllBytesAsync(capture, deadline.Token);
            actual.ShouldBe(expected, "composed-native-argv-exact-bytes");
            actual.Length.ShouldBe(expected.Length, "composed-native-argv-full-length");
        }
        finally
        {
            if (runner.Pid is { } pid)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await process.WaitForExitAsync(deadline.Token);
                }
                catch (ArgumentException) { /* already exited */ }
            }
            Directory.Delete(root, recursive: true);
        }
    }
}
