using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>Actual exit evidence controls classification; a process exit never settles a task.</summary>
public static class SessionExitPolicy
{
    public static bool IsRequested(SessionTerminationSource source) => source is
        SessionTerminationSource.OperatorRequest or SessionTerminationSource.SystemRequest
        or SessionTerminationSource.PolicyRefresh or SessionTerminationSource.CompactionContinuationRecovery;

    public static bool IsCleanStop(int? code, int? signal, AgentExitReason reason,
        SessionTerminationSource source = SessionTerminationSource.Unknown)
    {
        if (reason == AgentExitReason.MemoryKilled) return false;
        // CPU-spin reclamation and already-authorized stops are policy outcomes, never success.
        if (reason == AgentExitReason.CpuSpinKilled) return true;
        if (signal is > 0) return IsRequested(source);
        // Old producers retain lifecycle compatibility, without claiming a known normal exit.
        return code == 0;
    }

    public static string Describe(int? code, int? signal,
        SessionTerminationSource source = SessionTerminationSource.Unknown,
        ProcessStopIntentDto? intent = null)
    {
        var requested = IsRequested(source) || intent is { Provenance: ProcessStopProvenance.LiveIssuer,
            Target: ProcessStopTarget.Child or ProcessStopTarget.Custody };
        if (code is null && signal is null && intent is { Target: ProcessStopTarget.Host,
            Outcome: ProcessStopOutcome.Issued, Provenance: ProcessStopProvenance.LiveIssuer })
            return "Antiphon host stop issued; child exit unobserved (exit code unknown; signal unknown)";
        if (signal is > 0)
        {
            var death = $"killed by signal {signal} ({SignalName(signal.Value)})";
            if (CanAttribute(intent, signal))
            {
                var record = intent!.Durable ? "matching stop request recorded" : "stop record not durable";
                return $"stopped by Antiphon ({SignalName(signal.Value)}; {record}); {death}";
            }
            if (requested) return $"Antiphon stop requested; {death}; sender unconfirmed";
            return intent is { Provenance: ProcessStopProvenance.RecoveredUnverified }
                ? $"{death}; sender unconfirmed (untrusted stop record)"
                : $"{death}; sender unknown";
        }
        var exit = signal == 0 && code is not null
            ? $"process exited with exit code {code}"
            : $"process ended (exit code {code?.ToString() ?? "unknown"}; signal unknown)";
        if (!requested) return exit;
        return signal == 0 ? $"{exit}; Antiphon stop requested"
            : "Antiphon stop requested; signal unknown";
    }

    public static bool CanAttribute(ProcessStopIntentDto? intent, int? signal) => signal is > 0
        && intent is { Provenance: ProcessStopProvenance.LiveIssuer, Outcome: ProcessStopOutcome.Issued,
            Target: ProcessStopTarget.Child or ProcessStopTarget.Custody, TargetPid: > 0, TargetStartedAtUtc: not null }
        && intent.AttemptedSignal == signal && intent.RequestId != Guid.Empty
        && intent.IssuerBootId != Guid.Empty && intent.HostInstanceId != Guid.Empty;

    // POSIX signal names here describe the qualified Linux producer only.
    public static string SignalName(int signal) => signal switch
    {
        1 => "SIGHUP", 9 => "SIGKILL", 15 => "SIGTERM", _ => "UNKNOWN"
    };
}
