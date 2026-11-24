using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

internal static class ScriptHarnessHandleProbe
{
    internal readonly record struct RootExit(int Pid, int ExitCode, DateTime StartedUtc, DateTime ExitedUtc);

    internal static async Task<string[]> WaitAsync(string directory, string nonce, Func<RootExit?> readExit,
        IReadOnlyList<string> arguments, OutputTail stdout, OutputTail stderr)
    {
        var clock = Stopwatch.StartNew();
        var path = Path.Combine(directory, "probe-receipt");
        while (!File.Exists(path))
        {
            if (readExit() is { } earlyExit)
                throw await EarlyExitAsync(path, nonce, earlyExit, arguments, stdout, stderr);
            if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("Handle probe readiness deadline.");
            await Task.Delay(20);
        }
        var fields = File.ReadAllText(path).Split(' ');
        fields.Length.ShouldBe(7);
        fields[0].ShouldBe(nonce);
        if (readExit() is { } exit)
            throw await EarlyExitAsync(path, nonce, exit, arguments, stdout, stderr);
        return fields;
    }

    private static async Task<InvalidOperationException> EarlyExitAsync(string receipt, string nonce, RootExit exit,
        IReadOnlyList<string> arguments, OutputTail stdout, OutputTail stderr)
    {
        // A published receipt may have won the race with the last File.Exists check.
        var hasReceipt = File.Exists(receipt);
        var hasPartial = File.Exists(receipt + ".tmp");
        var reachedFixture = File.Exists(Path.Combine(Path.GetDirectoryName(receipt)!, "first-instruction"));
        try { await Task.WhenAll(stdout.Drain, stderr.Drain).WaitAsync(TimeSpan.FromSeconds(1)); }
        catch (Exception) { /* Preserve the root exit and report the drain states with the available output. */ }

        var message = FormattableString.Invariant($"""
            Root exited before handle probe completed.
            pid={exit.Pid}, exit code={exit.ExitCode}, lifetime={(exit.ExitedUtc - exit.StartedUtc).TotalMilliseconds:F3} ms, start time={exit.StartedUtc:O}
            arguments={JsonSerializer.Serialize(arguments)}
            receipt exists={hasReceipt}, partial receipt exists={hasPartial}, first instruction marker exists={reachedFixture}
            stdout ({stdout.Drain.Status}, last {OutputTail.Capacity} characters):
            {stdout.Read()}
            stderr ({stderr.Drain.Status}, last {OutputTail.Capacity} characters):
            {stderr.Read()}
            """);

        try
        {
            var testArguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(testArguments, "--results-directory");
            var results = index >= 0 && index + 1 < testArguments.Length ? Path.GetFullPath(testArguments[index + 1])
                : Path.Combine(AppContext.BaseDirectory, "TestResults");
            Directory.CreateDirectory(results);
            var evidence = Path.Combine(results, $"handle-probe-{nonce}.txt");
            File.WriteAllText(evidence, message);
            TestContext.Current?.Output.AttachArtifact(evidence, Path.GetFileName(evidence), "Handle probe root exit");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new InvalidOperationException(message + $"\nCould not save handle probe evidence: {error.Message}", error);
        }

        return new InvalidOperationException(message);
    }

    internal static void AssertUnrelatedEventWasNotInherited(string[] receipt) =>
        bool.Parse(receipt[3]).ShouldBeFalse("Child cannot signal the unrelated inheritable event.");

    internal sealed class OutputTail
    {
        internal const int Capacity = 4096;
        private readonly StringBuilder _tail = new(Capacity);
        internal Task Drain { get; }

        internal OutputTail(StreamReader reader) => Drain = DrainAsync(reader, this);

        internal static async Task DrainAsync(StreamReader reader, OutputTail? capture = null)
        {
            var buffer = new char[Capacity];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory())) != 0)
                capture?.Append(buffer, count);
        }

        private void Append(char[] buffer, int count)
        {
            lock (_tail)
            {
                var overflow = _tail.Length + count - Capacity;
                if (overflow > 0) _tail.Remove(0, overflow);
                _tail.Append(buffer, 0, count);
            }
        }

        internal string Read()
        {
            lock (_tail) return _tail.Length == 0 ? "(empty)" : _tail.ToString();
        }
    }
}
