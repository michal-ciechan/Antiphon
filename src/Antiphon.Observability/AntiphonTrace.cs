using System.Diagnostics;

namespace Antiphon.Observability;

/// <summary>
/// Internal OpenTelemetry-style spans for the server's own work (dispatcher ticks and sweeps,
/// task dispatch stages, worktree git, session launch). A span is created only while a listener
/// samples <see cref="SourceName"/>; nothing in production exports it (Program.cs exports only
/// <see cref="LiveTiming.SourceName"/>), so it costs one null check there. Test traces
/// (<c>ANTIPHON_TEST_TRACE_FILE</c>) record it.
/// Names and tags are closed, code-supplied labels: never a prompt, body, path or credential.
/// </summary>
public static class AntiphonTrace
{
    public const string SourceName = "Antiphon.Server";

    public static readonly ActivitySource Source = new(SourceName);

    public static Activity? Span(string name) => Source.StartActivity(name, ActivityKind.Internal);

    public static Activity? Span(string name, string key, object? value)
    {
        var activity = Source.StartActivity(name, ActivityKind.Internal);
        activity?.SetTag(key, value);
        return activity;
    }

    /// <summary>Awaits <paramref name="body"/> inside a span; a throw marks the span failed.</summary>
    public static async Task<T> Run<T>(string name, Func<Task<T>> body)
    {
        using var activity = Span(name);
        try { return await body(); }
        catch (Exception ex) { activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name); throw; }
    }

    public static async Task Run(string name, Func<Task> body)
    {
        using var activity = Span(name);
        try { await body(); }
        catch (Exception ex) { activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name); throw; }
    }
}
