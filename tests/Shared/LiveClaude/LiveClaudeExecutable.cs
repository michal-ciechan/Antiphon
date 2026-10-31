using System.Security.Cryptography;

namespace Antiphon.TestSupport.LiveClaude;

/// <summary>Test-side, fail-closed executable admission. Resolution never starts a process.</summary>
internal sealed class LiveClaudeExecutable
{
    private readonly LiveClaudeLaunchContext? context;
    public string Path { get; }
    private LiveClaudeExecutable(string path, LiveClaudeLaunchContext? context)
        => (Path, this.context) = (path, context);

    public static LiveClaudeExecutable Resolve(string uid, LiveClaudeLaunchRequest? request,
        LiveClaudeLaunchContext? context, Func<string> legacy)
    {
        if (!LiveClaudeCore.Contains(uid)) return new(legacy(), null);
        if (request is null || context is null) throw new LiveClaudeRefusal("missing-context");
        if (string.IsNullOrWhiteSpace(request.Executable)) throw new LiveClaudeRefusal("missing-exe");
        if (request.Binding.Uid != uid || context.Binding.Uid != uid) throw new LiveClaudeRefusal("context-uid");
        if (request.Binding.Run != context.Binding.Run) throw new LiveClaudeRefusal("context-run");
        if (request.Binding.Attempt != context.Binding.Attempt) throw new LiveClaudeRefusal("context-attempt");
        if (request.Binding.Source != context.Binding.Source) throw new LiveClaudeRefusal("context-source");
        if (request.Binding.Profile != context.Binding.Profile) throw new LiveClaudeRefusal("context-profile");
        var b = context.Binding;
        if (string.IsNullOrWhiteSpace(b.Run) || b.Attempt <= 0 || b.Source.Length != 40
            || !b.Source.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(b.Profile))
            throw new LiveClaudeRefusal("invalid-context");
        if (!SamePath(request.Executable, context.Image.Path)) throw new LiveClaudeRefusal("executable-mismatch");
        context = context with { Chain = context.Chain.ToArray() };
        var result = new LiveClaudeExecutable(context.Image.Path, context);
        result.Recheck();
        return result;
    }

    public LiveClaudeExecutableReceipt Start(Action<string> probe, Func<string, LiveClaudeProcess> start,
        Func<LiveClaudeProcess, LiveClaudeObservation?> observe)
    {
        Recheck();
        probe(Path);
        Recheck();
        var process = start(Path);
        var observation = observe(process);
        Validate(process, observation);
        return new(observation!);
    }

    public void Complete(LiveClaudeExecutableReceipt receipt,
        Func<LiveClaudeProcess, LiveClaudeObservation?> observe)
    {
        Validate(receipt.ExecutedImage.Process, observe(receipt.ExecutedImage.Process));
        Recheck();
    }

    private void Validate(LiveClaudeProcess process, LiveClaudeObservation? observation)
    {
        if (observation is null) throw new LiveClaudeRefusal("executable-mismatch");
        if (!SamePath(observation.Image.Path, Path)) throw new LiveClaudeRefusal("executable-mismatch");
        if (observation.Image.Sha256 != context!.Image.Sha256) throw new LiveClaudeRefusal("executable-mismatch");
        if (observation.Process.Pid != process.Pid) throw new LiveClaudeRefusal("executable-mismatch");
        if (observation.Process.Started != process.Started) throw new LiveClaudeRefusal("executable-mismatch");
        if (observation.Process.Session != process.Session) throw new LiveClaudeRefusal("executable-mismatch");
        if (process.Pid <= 0 || string.IsNullOrWhiteSpace(process.Started) || string.IsNullOrWhiteSpace(process.Session))
            throw new LiveClaudeRefusal("executable-mismatch");
        if (observation.Chain.Count != context.Chain.Count || !observation.Chain.SequenceEqual(context.Chain))
            throw new LiveClaudeRefusal("executable-mismatch");
        Recheck();
    }

    private void Recheck()
    {
        if (context is null) throw new LiveClaudeRefusal("missing-context");
        var current = context.ReadLock();
        if (!current.Held) throw new LiveClaudeRefusal("lock-released");
        if (string.IsNullOrWhiteSpace(context.LockOwner) || current.Owner != context.LockOwner)
            throw new LiveClaudeRefusal("lock-replaced");
        CheckImage(context.Image);
        foreach (var image in context.Chain) CheckImage(image);
    }

    private static void CheckImage(LiveClaudeImage image)
    {
        try
        {
            if (!System.IO.Path.IsPathFullyQualified(image.Path) || image.Sha256.Length != 64
                || !image.Sha256.All(Uri.IsHexDigit)) throw new LiveClaudeRefusal("executable-mismatch");
            // A link or a linked ancestor is not an owned immutable image path.
            for (var part = image.Path; part is not null; part = System.IO.Path.GetDirectoryName(part))
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new LiveClaudeRefusal("executable-mismatch");
            if (Digest(image.Path) != image.Sha256) throw new LiveClaudeRefusal("executable-mismatch");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new LiveClaudeRefusal("executable-mismatch"); }
    }

    private static bool SamePath(string a, string b)
        => System.IO.Path.IsPathFullyQualified(a) && System.IO.Path.IsPathFullyQualified(b)
            && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public string RequirePath() { Recheck(); return Path; }

    // Native image/session observation is deliberately uncommissioned. Never credit a launch
    // from a version string or an inferred PID. A future native adapter must supply real facts.
    public void RequireNativeObservation() => throw new LiveClaudeRefusal("executable-mismatch");

    public static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
