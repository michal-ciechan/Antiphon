using Antiphon.Messaging;
using Antiphon.Messaging.Client;

namespace Antiphon.E2E.Fixtures;

/// <summary>No DB, runner, manifest or arbitrary configuration override is accepted here.</summary>
internal sealed record DistillerCanaryOptions(string Root, string SpecialistSlug, DistillerCanaryApproval Approval)
{
    public string Repo => Path.Combine(Root, "repo");
    public void Validate()
    {
        DistillerCanaryGuard.ValidateOptIns(Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS"),
            Environment.GetEnvironmentVariable("ANTIPHON_DISTILLER_APPLY_CANARY"));
        Approval.Validate(DateTimeOffset.UtcNow);
        var owner = Path.Combine(AntiphonAppFixture.FindRepositoryRoot(), ".antiphon", "acceptance", "card-0419") + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(Root) || !Path.GetFullPath(Root).StartsWith(owner, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(Path.Combine(Repo, ".git"))) throw new InvalidOperationException("Canary requires its own retained repository.");
    }
    public void Configure(Dictionary<string, string?> settings)
    {
        settings["Agents:DefaultDefinition"] = "claude";
        settings["Git:WorkspacePath"] = Repo;
        settings["Git:WorktreeBasePath"] = Path.Combine(Root, "worktrees");
        settings["GitHub:Enabled"] = "false";
        settings["ChannelBridge:Enabled"] = "false";
        settings["AntiphonMessaging:BootstrapServers"] = "127.0.0.1:1";
        settings["Delegation:CheckInterpreterEnabled"] = "false";
        settings["Delegation:DiagnoseEnabled"] = "false";
        settings["Delegation:OutputDistillerEnabled"] = "true";
        settings["Delegation:OutputDistillerMode"] = "Apply";
        settings["Delegation:OutputDistillerAgentSlug"] = SpecialistSlug;
        settings["Delegation:OutputDistillerWorkingDirectory"] = Path.Combine(Repo, ".antiphon", "specialist");
        settings["Delegation:DistillMinChars"] = "4000";
        settings["Delegation:DistillMaxRawChars"] = "20000";
        settings["Delegation:OutputDistillerWaitSeconds"] = "45";
        settings["Delegation:DistilledMaxChars"] = "1500";
        settings["Delegation:DistilledMaxRatio"] = "0.6";
    }
}

internal sealed class RefusingCanaryMessaging : IAntiphonMessagingProducer, IAntiphonMessagingConsumer
{
    public Task SendAsync(ChannelReply reply, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Canary cannot send external messages.");
    public IAsyncEnumerable<ChannelMessage> ConsumeAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Canary cannot consume external messages.");
}
