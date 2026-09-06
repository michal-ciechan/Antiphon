using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

public sealed class GrokRulesHttpException(GrokRulesTransportException error)
    : HttpException(error.StatusCode, error.Message, error.Code);

public static class GrokRulesLaunchValidation
{
    public static void Validate(AgentLaunchSpec spec, GrokRulesSettings settings)
    {
        // Raw argv always wins, including Herdr's effective environment token semantics.
        GrokLaunchArgs.EnsureWindowsRulesArgv(spec.Args, spec.Kind, spec.Backend, spec.Env, "Grok rules launch");
        if (spec.GrokRulesPayload is not { } payload) return;
        try
        {
            settings.Validate();
            GrokRulesTransport.Encode(payload, spec.Kind == AgentKind.Grok, settings.MaxFileBytes);
            var effective = spec.Backend == SessionBackend.Herdr
                ? spec.Args.Select(arg => DollarEnvArg.TryResolve(arg, spec.Env, out var value) ? value : arg).ToArray()
                : spec.Args;
            if (GrokRulesTransport.HasExplicitRules(effective))
                throw new GrokRulesTransportException("grok_rules_source_conflict", "Move the append to SystemPromptAppend.");
        }
        catch (GrokRulesTransportException ex) { throw new GrokRulesHttpException(ex); }
    }
}
