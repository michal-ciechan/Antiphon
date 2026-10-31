using System.Security.Cryptography;

namespace Antiphon.TestSupport.LiveClaude;

internal sealed class LiveClaudeExecutable
{
    private readonly LiveClaudeLaunchContext? context;
    public string Path { get; }
    private LiveClaudeExecutable(string path, LiveClaudeLaunchContext? context)
        => (Path, this.context) = (path, context);

    // First commit deliberately preserves the old PATH selection and absent observation gates.
    public static LiveClaudeExecutable Resolve(string uid, LiveClaudeLaunchRequest? request,
        LiveClaudeLaunchContext? context, Func<string> legacy)
        => new(legacy(), context);

    public LiveClaudeExecutableReceipt Start(Action<string> probe, Func<string, LiveClaudeProcess> start,
        Func<LiveClaudeProcess, LiveClaudeObservation?> observe)
    {
        probe(Path);
        var process = start(Path);
        var observation = observe(process) ?? new LiveClaudeObservation(process, new(Path, ""), []);
        return new(observation);
    }

    public void Complete(LiveClaudeExecutableReceipt receipt,
        Func<LiveClaudeProcess, LiveClaudeObservation?> observe) { }

    public static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
