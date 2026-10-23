using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Shouldly;

namespace Antiphon.Tests.Agents;

/// <summary>
/// The scripted runner owns a generation and input history just like the guarded backend.
/// Invalid requests fail the fixture before they can reach its composer or modal model.
/// </summary>
internal sealed class ScriptedClaudeInputGuard
{
    private readonly Guid _runtimeEpoch = Guid.NewGuid();
    private long _inputRevision;
    public DateTime AcceptedStartedAt { get; private set; } = DateTime.UnixEpoch;

    public void Bind(DateTime? acceptedStartedAt)
    {
        acceptedStartedAt.ShouldNotBeNull("the scripted launch must carry its accepted generation");
        AcceptedStartedAt = acceptedStartedAt.Value;
    }

    public ProviderDialogObservation Observe(Guid sessionId, long sequence, string screen)
    {
        var observation = ClaudeDialogObservation.Observe(screen);
        var state = observation.State switch
        {
            ClaudeDialogState.Clear => ProviderDialogState.Clear,
            ClaudeDialogState.Blocked => ProviderDialogState.Blocked,
            _ => ProviderDialogState.Unknown,
        };
        return new(sessionId, _runtimeEpoch, AcceptedStartedAt, sequence, _inputRevision, state,
            observation.Kind.ToString(), observation.CanCancel, InputHistoryKnown: true);
    }

    public async Task<RunnerConditionalInputResult> WriteAsync(Guid sessionId,
        RunnerConditionalInputRequest request, long sequence, string screen,
        Func<string, CancellationToken, Task> write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        request.ExpectedAcceptedStartedAt.ShouldBe(AcceptedStartedAt);
        request.ExpectedLastSequence.ShouldBe(sequence);
        request.ExpectedRuntimeEpoch.ShouldBe((Guid?)_runtimeEpoch);
        request.ExpectedInputRevision.ShouldBe((long?)_inputRevision);
        request.RequireClaudeDialogClear.ShouldBeTrue();
        switch (request.DialogIntent)
        {
            case ClaudeDialogInputIntent.None:
                Observe(sessionId, sequence, screen).State.ShouldBe(ProviderDialogState.Clear);
                break;
            case ClaudeDialogInputIntent.StartupTrust:
                ClaudeDialogActions.AllowsTrustKey(screen, request.Input).ShouldBeTrue();
                break;
            case ClaudeDialogInputIntent.StartupEffortKeep:
                ClaudeDialogActions.AllowsEffortKey(screen, request.Input, ClaudeEffortOption.Keep).ShouldBeTrue();
                break;
            case ClaudeDialogInputIntent.StartupEffortSwitch:
                ClaudeDialogActions.AllowsEffortKey(screen, request.Input, ClaudeEffortOption.Switch).ShouldBeTrue();
                break;
            default:
                throw new InvalidOperationException("Unexpected scripted startup intent: " + request.DialogIntent);
        }
        _inputRevision++;
        await write(request.Input, ct);
        return new(sessionId, ConditionalInputOutcomes.Written, AcceptedStartedAt, sequence + 1);
    }

    public static string Composer(string draft = "") =>
        "────────────────────────────────────────\n> " + draft
        + "\n────────────────────────────────────────\n? for shortcuts";
}
