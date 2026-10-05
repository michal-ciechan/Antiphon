using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[Category("Unit")]
public sealed class PushProbeOutcomeTests
{
    [Test]
    public void Classifies_a_disabled_prompt_as_a_missing_credential()
    {
        foreach (var stderr in new[] { "fatal: terminal prompts disabled", "could not read Username", "could not read Password" })
            PushProbeOutcome.Classify(128, stderr).ShouldBe(PushProbeCategory.CredentialMissing);
        PushProbeOutcome.Remedy(PushProbeCategory.CredentialMissing).ShouldContain("PhoneHome:AllowedCloneSources");
        PushProbeOutcome.Remedy(PushProbeCategory.CredentialMissing).ShouldContain("docs/agent-credentials.md");
        AssertRotationRemedy();
    }

    [Test]
    public void Classifies_an_authentication_failure_as_a_rejected_credential()
    {
        foreach (var stderr in new[] { "fatal: Authentication failed", "remote: Invalid username or token", "Bad credentials", "fatal: error: 401" })
            PushProbeOutcome.Classify(128, stderr).ShouldBe(PushProbeCategory.CredentialRejected);
        PushProbeOutcome.Remedy(PushProbeCategory.CredentialRejected).ShouldContain("refresh the server2 file");
        AssertRotationRemedy();
    }

    [Test]
    public void Classifies_a_permission_denial_as_forbidden()
    {
        foreach (var stderr in new[] { "remote: Permission to owner/repo.git denied to account.", "error: 403", "Permission denied (publickey)", "deploy key cannot push" })
            PushProbeOutcome.Classify(128, stderr).ShouldBe(PushProbeCategory.Forbidden);
        PushProbeOutcome.Remedy(PushProbeCategory.Forbidden).ShouldContain("grant the account access");
    }

    [Test]
    public void Classifies_a_network_failure_as_unreachable()
    {
        foreach (var stderr in new[] { "Could not resolve host", "Failed to connect", "Connection timed out", "Connection refused", "ssh: connect to host", "SSL certificate problem" })
            PushProbeOutcome.Classify(128, stderr).ShouldBe(PushProbeCategory.Unreachable);
        PushProbeOutcome.Remedy(PushProbeCategory.Unreachable).ShouldContain("backoff");
        PushProbeOutcome.Classify(128, "unrecognized failure").ShouldBe(PushProbeCategory.Unknown);
        PushProbeOutcome.Remedy(PushProbeCategory.Unknown).ShouldBe("check the runner log");
    }

    [Test]
    public void Zero_exit_is_authorized_whatever_stderr_says()
    {
        foreach (var stderr in new[] { "", "terminal prompts disabled", "Authentication failed", "Permission to repo denied", "Could not resolve host" })
            PushProbeOutcome.Classify(0, stderr).ShouldBe(PushProbeCategory.Authorized);
        PushProbeOutcome.Remedy(PushProbeCategory.Authorized).ShouldBeEmpty();
    }

    private static void AssertRotationRemedy()
    {
        foreach (var category in Enum.GetValues<PushProbeCategory>())
            PushProbeOutcome.Remedy(category).Contains("github-push-token", StringComparison.Ordinal)
                .ShouldBe(category == PushProbeCategory.CredentialRejected);
    }
}
