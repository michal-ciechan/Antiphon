using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>New admissions only. Recorded workspace mode is independent of this gate.</summary>
public sealed class RefOnlyDesktopGate(IOptionsMonitor<RefOnlyDesktopSettings> monitor, ILogger<RefOnlyDesktopGate> logger)
{
    public bool Admits(AgentTask task) => false;
}
