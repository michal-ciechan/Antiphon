using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Composes the session-scoped launch identity and instructions for an agent launch.
/// Both cardless and card-backed sessions use this so a card session has the same
/// bundle contract and delegation credential as a standing interactive session.
/// </summary>
public sealed class AgentSessionLaunchComposer
{
    private readonly AppDbContext _db;
    private readonly DelegationSettings _delegationSettings;
    private readonly AgentRegistry _agentRegistry;
    private readonly AgentTuiLaunchResolver? _launchResolver;
    private readonly ILogger<AgentSessionLaunchComposer> _logger;

    public AgentSessionLaunchComposer(
        AppDbContext db,
        IOptions<DelegationSettings> delegationSettings,
        AgentRegistry agentRegistry,
        ILogger<AgentSessionLaunchComposer> logger,
        AgentTuiLaunchResolver? launchResolver = null)
    {
        _db = db;
        _delegationSettings = delegationSettings.Value;
        _agentRegistry = agentRegistry;
        _logger = logger;
        _launchResolver = launchResolver;
    }

    public async Task<AgentLaunchComposition> ComposeForAgentAsync(Agent agent, CancellationToken ct)
    {
        var (delegationToken, delegationTokenHash) = AgentTaskService.NewToken();
        var extraEnv = new Dictionary<string, string>
        {
            ["ANTIPHON_API"] = _delegationSettings.ApiBaseUrl,
            ["ANTIPHON_AGENT_ID"] = agent.Id.ToString("D"),
            ["ANTIPHON_TASK_TOKEN"] = delegationToken,
        };

        var profileKind = await PeekProfileKindAsync(agent, ct);
        var isClaudeCode = profileKind == AgentKind.ClaudeCode;
        var isGrok = profileKind == AgentKind.Grok;
        var isCodex = profileKind == AgentKind.Codex;
        var extraArgs = new List<string>();
        string? composedStamp = null;
        if (isClaudeCode || isGrok || isCodex)
        {
            var sessionName = agent.Name.Trim();
            if (isClaudeCode && sessionName.Length > 0)
                extraArgs.AddRange(["--name", sessionName]);

            if (isCodex)
            {
                extraArgs.AddRange([
                    CodexLaunchArgs.ConfigFlag,
                    CodexLaunchArgs.ReasoningEffortOverride(agent.ModelLevel),
                    CodexLaunchArgs.ConfigFlag,
                    CodexLaunchArgs.DisablePasteBurst,
                ]);
            }

            var attachedKeys = await AgentBundleAttachments.LoadAsync(_db, agent.Id, _logger, ct);
            var composed = InstructionBundleComposer.Compose(
                attachedKeys,
                AgentReplyStyles.ComposedKey(agent.ReplyStyle),
                agent.SystemPromptAppend);
            composedStamp = composed.StampLine;
            if (!composed.IsEmpty)
            {
                var boundChannels = await _db.ChatChannels
                    .Where(c => c.AgentId == agent.Id && c.Enabled)
                    .Select(c => new { c.Provider, c.Title, c.ExternalId })
                    .ToListAsync(ct);
                var rendered = ChannelPreamble.Render(
                    composed.Text,
                    agent.Name,
                    boundChannels.Select(c => (c.Provider, c.Title ?? c.ExternalId)).ToList());
                InstructionBundleComposer.EnsureWithinCommandLineBudget(
                    composed with { Text = rendered },
                    extraArgs,
                    _delegationSettings.CommandLineBudgetChars,
                    $"Agent '{agent.Name}'");
                extraArgs.AddRange(isCodex
                    ? [CodexLaunchArgs.ConfigFlag, CodexLaunchArgs.DeveloperInstructions(rendered)]
                    : new[] { isGrok ? "--rules" : "--append-system-prompt", rendered });
            }
        }

        return new AgentLaunchComposition(extraEnv, extraArgs, delegationTokenHash, composedStamp);
    }

    public async Task<AgentKind?> PeekProfileKindAsync(Agent agent, CancellationToken ct)
    {
        if (agent.TuiProfileId is { } profileId)
        {
            return await _db.AgentTuiProfiles.AsNoTracking()
                .Where(profile => profile.Id == profileId)
                .Select(profile => (AgentKind?)profile.Kind)
                .FirstOrDefaultAsync(ct);
        }

        if (_launchResolver is not null)
        {
            var defaultProfileKind = await _db.AgentTuiProfiles.AsNoTracking()
                .Where(profile => profile.IsDefault)
                .Select(profile => (AgentKind?)profile.Kind)
                .FirstOrDefaultAsync(ct);
            if (defaultProfileKind is not null)
                return defaultProfileKind;
        }

        return Enum.TryParse<AgentKind>(
            _agentRegistry.LookupByName(_agentRegistry.Settings.DefaultDefinition).Kind,
            ignoreCase: true,
            out var legacyKind)
            ? legacyKind
            : null;
    }
}

public sealed record AgentLaunchComposition(
    IReadOnlyDictionary<string, string> ExtraEnv,
    IReadOnlyList<string> ExtraArgs,
    string DelegationTokenHash,
    string? ComposedStamp);
