using Antiphon.SessionRunner;
using TUnit.Core.Exceptions;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0383 V4 eligibility: headed opt-in, a live herdr that answers ping, and a real grok.exe.
/// A leftover <c>herdr.sock</c> file is not evidence the pipe accepts connections.
/// </summary>
internal static class HerdrLiveSession
{
    public const string EnvFlag = "ANTIPHON_HEADED_TESTS";

    public static string GrokExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "bin", "grok.exe");

    public static string DefaultSocketPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "herdr", "herdr.sock");

    public static async Task SkipIfNotEligibleAsync(CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable(EnvFlag) != "1")
            throw new SkipTestException($"Set {EnvFlag}=1 to opt in to headed herdr tests");
        if (!File.Exists(GrokExePath))
            throw new SkipTestException($"grok.exe not found at {GrokExePath}");

        var client = new HerdrClient(new HerdrSettings { Enabled = true, ConnectTimeoutMs = 1_000 });
        try
        {
            await client.ConnectAndValidateAsync(ct);
        }
        catch (Exception ex) when (ex is not SkipTestException and not OperationCanceledException)
        {
            throw new SkipTestException(
                $"herdr is not reachable at {DefaultSocketPath} ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
