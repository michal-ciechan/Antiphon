using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// An in-process stand-in for the operator commands' HTTP transport (unit 21 of the 2026-11-22 plan): every request is recorded
/// (method, URI, headers, body) and answered by the test's function; nothing leaves the process.
/// </summary>
internal sealed class RecordingOperatorHttp(Func<RecordingOperatorHttp.Call, NightlyReportHttpResponse> answer)
{
    internal sealed record Call(string Method, string Uri, IReadOnlyDictionary<string, string> Headers, string? Body)
    {
        public string Path => new Uri(Uri).PathAndQuery;
    }

    public List<Call> Calls { get; } = [];

    public NightlyReportHttp Transport => (method, uri, headers, body) =>
    {
        var call = new Call(method, uri, new Dictionary<string, string>(headers ?? new Dictionary<string, string>()), body);
        lock (Calls) Calls.Add(call);
        return answer(call);
    };
}
