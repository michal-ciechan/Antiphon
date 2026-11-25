using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Application;

/// <summary>
/// Runs independent world cases side by side (Codex CLI C959/C1029 worlds, C1213 branch-delivery worlds); every case runs and every failure is reported under its label.
/// A world's only child processes are git (its phone-home host is in-process Kestrel and its recipient terminals are
/// in-process fakes), but each also has a Kestrel host, a harness and a schema, so this class (one strict
/// ProcessSpawnLimit slot) runs at most Clamp(ProcessorCount / 2, 1, 4) worlds at once; ANTIPHON_TEST_CODEX_CLI_WORLDS
/// (1-4, clamped; ANTIPHON_TEST_C1029_WORLDS is still read when it is unset or blank) overrides it for both the C959
/// and C1029 classes, and 1 runs the cases one at a time (docs/testing-and-build.md, process-spawning lanes).
/// Sample timing: a never-refuses create leg reads its kit's fake clock after the kit exists, but the dispatch,
/// warm-reuse and remote-delivery legs sample wall-clock time, the remote one before its world is built. Those
/// vectors (clock-skew 2 minutes ahead, staleness past 15 minutes) need a margin of minutes over world creation.
/// Shared across worlds: RunnerBindingInvalidation's process-wide listeners, so one world's binding write drops
/// another's routing cache; that costs a re-resolve from its own database, never a wrong answer.
/// </summary>
internal static class Worlds
{
    public const string OverrideVariable = "ANTIPHON_TEST_CODEX_CLI_WORLDS";
    public const string LegacyOverrideVariable = "ANTIPHON_TEST_C1029_WORLDS";
    public static int Degree { get; } = int.TryParse(Setting()?.Trim(), out var n)
        ? Math.Clamp(n, 1, 4) : Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

    private static string? Setting() =>
        Environment.GetEnvironmentVariable(OverrideVariable) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value : Environment.GetEnvironmentVariable(LegacyOverrideVariable);

    public static Task InParallelAsync(IEnumerable<(string Name, Func<Task> Body)> cases) =>
        InParallelAsync(cases, c => c.Name, c => c.Body());

    public static async Task InParallelAsync<T>(IEnumerable<T> cases, Func<T, string> label, Func<T, Task> body)
    {
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Degree }, async (c, _) =>
        {
            var name = label(c);
            using var span = TestTrace.Span("world case", "world.case", name);
            try { await body(c); }
            catch (Exception ex)
            {
                span?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.GetType().Name);
                Console.WriteLine($"WORLD CASE FAILED {name}: {ex.GetType().Name}: {ex.Message}");
                failures.Enqueue(new InvalidOperationException("World case " + name + ": " + Cause(ex.Message), ex));
            }
        });
        if (!failures.IsEmpty)
            throw new AggregateException($"{failures.Count} world case(s) failed", failures);
    }

    // A short cause for the summary: up to five lines (a Shouldly expression, "should be", expected, "but was" and
    // actual), plus the Shouldly custom message (the guard id, e.g. C959-pc-190) after the last "Additional Info:".
    // The full message stays on the inner exception and the console line.
    private static string Cause(string message)
    {
        var lines = message.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return "";
        var info = lines.LastIndexOf("Additional Info:");
        var head = info == 0 ? "(no description)" : string.Join(" ", lines.Take(info > 0 ? Math.Min(info, 5) : 5));
        return info >= 0 && info + 1 < lines.Count ? head + " [" + string.Join(" ", lines.Skip(info + 1)) + "]" : head;
    }
}
