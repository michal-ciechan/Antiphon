using Microsoft.Extensions.Options;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Infrastructure.Agents.Pty;

/// <summary>
/// Concrete (no interface — not an external I/O seam) lookup + resolution helper for
/// configured agent definitions. Owns the conversion from
/// <c>(definitionName, AgentLaunchOptions)</c> to a fully-formed <see cref="AgentLaunchSpec"/>.
/// Adapters never read configuration directly.
/// </summary>
public sealed class AgentRegistry
{
    private readonly IOptionsMonitor<AgentRegistrySettings> _options;

    public AgentRegistry(IOptionsMonitor<AgentRegistrySettings> options)
    {
        _options = options;
    }

    public AgentRegistrySettings Settings => _options.CurrentValue;

    public AgentDefinition LookupByName(string definitionName)
    {
        if (string.IsNullOrWhiteSpace(definitionName))
            throw new ArgumentException("Definition name must be provided.", nameof(definitionName));

        var definitions = _options.CurrentValue.Definitions;
        if (!definitions.TryGetValue(definitionName, out var def))
            throw new NotFoundException(nameof(AgentDefinition), definitionName);

        return def;
    }

    public AgentLaunchSpec Resolve(string definitionName, AgentLaunchOptions options)
    {
        var def = LookupByName(definitionName);

        if (!Enum.TryParse<AgentKind>(def.Kind, ignoreCase: true, out var kind))
        {
            throw new InvalidOperationException(
                $"AgentDefinition '{definitionName}' has unknown Kind '{def.Kind}'. Validator should have rejected this at startup.");
        }

        var args = new List<string>(def.ArgsTemplate);
        if (options.ExtraArgs is not null)
            args.AddRange(options.ExtraArgs);

        var env = new Dictionary<string, string>(def.Env, StringComparer.Ordinal);
        if (options.ExtraEnv is not null)
        {
            foreach (var (k, v) in options.ExtraEnv)
                env[k] = v;
        }

        var cwd = string.IsNullOrWhiteSpace(options.Cwd)
            ? Environment.CurrentDirectory
            : options.Cwd;

        if (options.Cols <= 0)
            throw new ArgumentException("Cols must be positive.", nameof(options));
        if (options.Rows <= 0)
            throw new ArgumentException("Rows must be positive.", nameof(options));

        return new AgentLaunchSpec(
            DefinitionName: definitionName,
            Kind: kind,
            Exe: def.Exe,
            Args: args.AsReadOnly(),
            Env: env,
            Cwd: cwd,
            Cols: options.Cols,
            Rows: options.Rows);
    }
}
