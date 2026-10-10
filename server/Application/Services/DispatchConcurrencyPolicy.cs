using System.Globalization;
using System.Text;
using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0505. Pure resolution, counting and admission. No clock, database or static mutable state.
/// </summary>
public static class DispatchConcurrencyPolicy
{
    public const int ParallelMin = 1;
    public const int ParallelMax = 512;
    public const int QueuedMin = 0;
    public const int QueuedMax = 4096;
    public const string SourceDefault = "default";
    public const string SourceGlobal = "global";
    public const string SourceProject = "project";
    public const string SeedOrigin = "startupConfiguration";
    public const string PopulationOpen = "open";
    public const string PopulationQueued = "queued";
    public const string PopulationParallel = "parallel";
    public const string AxisAbsolute = "absolute";
    public const string AxisRole = "role";
    public const string CreateAdvisoryKey = DelegationOpenGate.AdvisoryLockKey;
    public const string ParallelAdvisoryKey = "antiphon.delegation.parallel-tasks";

    public static readonly AgentTaskRole[] OrdinaryRoles = Enum.GetValues<AgentTaskRole>()
        .Where(role => !AgentTaskRoles.IsSpecialist(role))
        .OrderBy(role => (int)role)
        .ToArray();

    private static readonly JsonSerializerOptions Canonical = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static DispatchConcurrencyDocument ImportSeed(DelegationSettings settings, DateTime importedAt)
    {
        var roles = new Dictionary<AgentTaskRole, RoleDocument>();
        foreach (var role in OrdinaryRoles)
        {
            roles[role] = new RoleDocument(
                new LimitField(true, settings.RecommendedInFlightFor(role)),
                new LimitField(true, null));
        }

        return new DispatchConcurrencyDocument(
            ModeSpecified: true,
            Mode: DispatchConcurrencyMode.LegacyOpen,
            MaxParallel: new LimitField(true, settings.MaxOpenTasks),
            MaxQueued: new LimitField(true, null),
            Roles: roles,
            ImportedAt: importedAt);
    }

    public static string WriteDocument(DispatchConcurrencyDocument document, bool includeAbsentRoles) =>
        WriteCanonical(document, includeAbsentRoles);

    public static EffectivePolicy Resolve(
        DispatchConcurrencyDocument seed,
        DispatchConcurrencyDocument? globalOverrides,
        DispatchConcurrencyDocument? projectOverrides,
        long globalRevision,
        long projectRevision)
    {
        var mode = ResolveMode(seed, globalOverrides, projectOverrides);
        var parallel = ResolveLimit(seed.MaxParallel, globalOverrides?.MaxParallel, projectOverrides?.MaxParallel, allowNull: false);
        var queued = ResolveLimit(seed.MaxQueued, globalOverrides?.MaxQueued, projectOverrides?.MaxQueued, allowNull: true);
        var roles = new List<ResolvedRole>(OrdinaryRoles.Length);
        foreach (var role in OrdinaryRoles)
        {
            seed.Roles.TryGetValue(role, out var seeded);
            RoleDocument? global = null;
            RoleDocument? project = null;
            globalOverrides?.Roles.TryGetValue(role, out global);
            projectOverrides?.Roles.TryGetValue(role, out project);
            var roleParallel = ResolveLimit(
                seeded?.MaxParallel ?? new LimitField(true, null),
                global?.MaxParallel,
                project?.MaxParallel,
                allowNull: true);
            var roleQueued = ResolveLimit(
                seeded?.MaxQueued ?? new LimitField(true, null),
                global?.MaxQueued,
                project?.MaxQueued,
                allowNull: true);
            roles.Add(new ResolvedRole(role, roleParallel.Value, roleParallel.Source, roleQueued.Value, roleQueued.Source));
        }

        return new EffectivePolicy(
            mode.Value, mode.Source,
            parallel.Value ?? seed.MaxParallel.Value ?? 0, parallel.Source,
            queued.Value, queued.Source,
            roles, globalRevision, projectRevision);
    }

    public static PopulationSnapshot Count(IEnumerable<TaskPopulationRow> rows, Guid? projectId)
    {
        var scoped = rows
            .Where(row => row.ProjectId == projectId && !AgentTaskRoles.IsSpecialist(row.Role))
            .ToList();
        var open = scoped.Where(IsOpen).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList();
        var parallel = scoped.Where(IsParallel).OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList();
        var queued = scoped.Where(row => row.Status == AgentTaskStatus.Queued)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList();
        var roles = new Dictionary<AgentTaskRole, RolePopulation>();
        foreach (var role in OrdinaryRoles)
        {
            roles[role] = new RolePopulation(
                open.Count(row => row.Role == role),
                parallel.Count(row => row.Role == role),
                queued.Count(row => row.Role == role));
        }

        return new PopulationSnapshot(open.Count, parallel.Count, queued.Count, roles, open, parallel, queued);
    }

    public static DispatchDecision DecideCreate(
        EffectivePolicy policy,
        PopulationSnapshot population,
        AgentTaskRole role,
        bool ignoreConcurrencyLimit)
    {
        if (AgentTaskRoles.IsSpecialist(role))
            return Admit(policy);

        var exceeded = new List<DispatchConstraint>();
        AddQueueConstraints(exceeded, policy, population, role);
        if (policy.Mode == DispatchConcurrencyMode.LegacyOpen)
            AddOpenConstraints(exceeded, policy, population, role);

        if (exceeded.Any(item => item.Population == PopulationQueued))
        {
            var primary = Primary(exceeded, PopulationQueued);
            return Refuse(policy, population, primary, exceeded, canOverride: false);
        }

        if (exceeded.Count > 0)
        {
            if (ignoreConcurrencyLimit)
                return Admit(policy);
            var primary = Primary(exceeded, PopulationOpen);
            return Refuse(policy, population, primary, exceeded, canOverride: true);
        }

        return Admit(policy);
    }

    public static DispatchDecision DecideDispatch(
        EffectivePolicy policy,
        PopulationSnapshot population,
        AgentTaskRole role)
    {
        if (AgentTaskRoles.IsSpecialist(role) || policy.Mode == DispatchConcurrencyMode.LegacyOpen)
            return Admit(policy);

        var exceeded = new List<DispatchConstraint>();
        AddParallelConstraints(exceeded, policy, population, role);
        if (exceeded.Count == 0)
            return Admit(policy);
        var primary = Primary(exceeded, PopulationParallel);
        return Refuse(policy, population, primary, exceeded, canOverride: false);
    }

    public static IReadOnlyList<PolicyValidationError> ValidateOverrides(JsonElement root)
    {
        var errors = new List<PolicyValidationError>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new PolicyValidationError("overrides", "overrides must be a JSON object."));
            return errors;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errors.Add(new PolicyValidationError(property.Name, $"Duplicate field '{property.Name}'."));
                continue;
            }

            switch (property.Name)
            {
                case "schemaVersion":
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt32(out var version)
                        || version != 1)
                    {
                        errors.Add(new PolicyValidationError(property.Name, "schemaVersion must be 1."));
                    }
                    break;
                case "mode":
                    ValidateMode(property.Value, errors);
                    break;
                case "maxParallel":
                    ValidateParallel(property.Value, "maxParallel", allowNull: false, errors);
                    break;
                case "maxQueued":
                    ValidateQueued(property.Value, "maxQueued", errors);
                    break;
                case "roles":
                    ValidateRoles(property.Value, errors);
                    break;
                default:
                    errors.Add(new PolicyValidationError(property.Name, $"Unknown field '{property.Name}'."));
                    break;
            }
        }

        return errors;
    }

    /// <summary>
    /// Structural parse of a stored document. Out-of-range legacy seed values stay readable;
    /// <see cref="ValidateOverrides"/> is what rejects them on a new write. Seed envelopes may
    /// carry <c>origin</c> and <c>importedAt</c> beside the policy fields.
    /// </summary>
    public static bool TryParseOverrides(JsonElement root, out DispatchConcurrencyDocument document)
    {
        document = EmptyDocument();
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        var modeSpecified = false;
        DispatchConcurrencyMode? mode = null;
        var maxParallel = new LimitField(false, null);
        var maxQueued = new LimitField(false, null);
        var roles = new Dictionary<AgentTaskRole, RoleDocument>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                return false;
            switch (property.Name)
            {
                case "schemaVersion":
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt32(out var version)
                        || version != 1)
                        return false;
                    break;
                case "origin":
                case "importedAt":
                    break;
                case "mode":
                    if (property.Value.ValueKind != JsonValueKind.String
                        || !Enum.TryParse(property.Value.GetString(), ignoreCase: false, out DispatchConcurrencyMode parsed)
                        || !Enum.IsDefined(parsed))
                        return false;
                    modeSpecified = true;
                    mode = parsed;
                    break;
                case "maxParallel":
                    if (!TryIntegral(property.Value, out var parallel))
                        return false;
                    maxParallel = new LimitField(true, parallel);
                    break;
                case "maxQueued":
                    if (!TryQueued(property.Value, out var queued))
                        return false;
                    maxQueued = new LimitField(true, queued);
                    break;
                case "roles":
                    if (!TryParseRoles(property.Value, roles))
                        return false;
                    break;
                default:
                    return false;
            }
        }

        document = new DispatchConcurrencyDocument(modeSpecified, mode, maxParallel, maxQueued, roles, null);
        return true;
    }

    private static bool TryQueued(JsonElement value, out int? number)
    {
        number = null;
        if (value.ValueKind == JsonValueKind.Null)
            return true;
        if (!TryIntegral(value, out var parsed))
            return false;
        number = parsed;
        return true;
    }

    private static bool TryParseRoles(JsonElement value, Dictionary<AgentTaskRole, RoleDocument> roles)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
                return false;
            if (IsNumericToken(property.Name)
                || !Enum.TryParse(property.Name, ignoreCase: false, out AgentTaskRole role)
                || !Enum.IsDefined(role)
                || AgentTaskRoles.IsSpecialist(role)
                || property.Value.ValueKind != JsonValueKind.Object)
                return false;

            var roleParallel = new LimitField(false, null);
            var roleQueued = new LimitField(false, null);
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in property.Value.EnumerateObject())
            {
                if (!fields.Add(field.Name))
                    return false;
                if (field.NameEquals("maxParallel"))
                {
                    if (!TryQueued(field.Value, out var parallel))
                        return false;
                    roleParallel = new LimitField(true, parallel);
                }
                else if (field.NameEquals("maxQueued"))
                {
                    if (!TryQueued(field.Value, out var queued))
                        return false;
                    roleQueued = new LimitField(true, queued);
                }
                else
                {
                    return false;
                }
            }

            roles[role] = new RoleDocument(roleParallel, roleQueued);
        }

        return true;
    }

    public static string CanonicalOverrides(JsonElement root)
    {
        TryParseOverrides(root, out var document);
        return WriteCanonical(document, includeAbsentRoles: false);
    }

    public static string CanonicalOverrides(DispatchConcurrencyDocument document) =>
        WriteCanonical(document, includeAbsentRoles: false);

    public static bool SameOverrides(string storedJson, JsonElement incoming)
    {
        using var stored = JsonDocument.Parse(storedJson);
        return string.Equals(CanonicalOverrides(stored.RootElement), CanonicalOverrides(incoming), StringComparison.Ordinal);
    }

    public static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static DispatchConcurrencyDocument EmptyDocument() =>
        new(false, null, new LimitField(false, null), new LimitField(false, null),
            new Dictionary<AgentTaskRole, RoleDocument>(), null);

    public static bool IsOpen(TaskPopulationRow row) =>
        row.Status is AgentTaskStatus.Queued or AgentTaskStatus.Dispatched or AgentTaskStatus.Working;

    public static bool IsParallel(TaskPopulationRow row) =>
        row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working;

    private static void AddOpenConstraints(
        List<DispatchConstraint> exceeded, EffectivePolicy policy, PopulationSnapshot population, AgentTaskRole role)
    {
        if (population.Open >= policy.MaxParallel)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationOpen, AxisAbsolute, null, population.Open, policy.MaxParallel, policy.ParallelSource));
        }

        var resolved = policy.Role(role);
        if (resolved.MaxParallel is int limit && population.Roles[role].Open >= limit)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationOpen, AxisRole, role.ToString(), population.Roles[role].Open, limit, resolved.ParallelSource));
        }
    }

    private static void AddQueueConstraints(
        List<DispatchConstraint> exceeded, EffectivePolicy policy, PopulationSnapshot population, AgentTaskRole role)
    {
        if (policy.MaxQueued is int projectLimit && population.Queued >= projectLimit)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationQueued, AxisAbsolute, null, population.Queued, projectLimit, policy.QueuedSource));
        }

        var resolved = policy.Role(role);
        if (resolved.MaxQueued is int limit && population.Roles[role].Queued >= limit)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationQueued, AxisRole, role.ToString(), population.Roles[role].Queued, limit, resolved.QueuedSource));
        }
    }

    private static void AddParallelConstraints(
        List<DispatchConstraint> exceeded, EffectivePolicy policy, PopulationSnapshot population, AgentTaskRole role)
    {
        if (population.Parallel >= policy.MaxParallel)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationParallel, AxisAbsolute, null, population.Parallel, policy.MaxParallel, policy.ParallelSource));
        }

        var resolved = policy.Role(role);
        if (resolved.MaxParallel is int limit && population.Roles[role].Parallel >= limit)
        {
            exceeded.Add(new DispatchConstraint(
                PopulationParallel, AxisRole, role.ToString(), population.Roles[role].Parallel, limit, resolved.ParallelSource));
        }
    }

    private static DispatchConstraint Primary(IReadOnlyList<DispatchConstraint> exceeded, string population)
    {
        var matches = exceeded.Where(item => item.Population == population).ToList();
        return matches.FirstOrDefault(item => item.Axis == AxisAbsolute) ?? matches[0];
    }

    private static DispatchDecision Admit(EffectivePolicy policy) =>
        new(true, null, null, false, 0, 0, null, 0, [], [], 0, 0, policy);

    private static DispatchDecision Refuse(
        EffectivePolicy policy,
        PopulationSnapshot population,
        DispatchConstraint primary,
        IReadOnlyList<DispatchConstraint> exceeded,
        bool canOverride)
    {
        var rows = primary.Population switch
        {
            PopulationQueued => population.QueuedRows,
            PopulationParallel => population.ParallelRows,
            _ => population.OpenRows,
        };
        if (primary.Axis == AxisRole && primary.Role is not null
            && Enum.TryParse<AgentTaskRole>(primary.Role, out var role))
        {
            rows = rows.Where(row => row.Role == role).ToList();
        }

        var ordered = rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToList();
        var listed = ordered.Take(12).Select(row => row.Id).ToList();
        var remaining = Math.Max(0, primary.Limit - primary.Count);
        var overage = Math.Max(0, primary.Count - primary.Limit);
        return new DispatchDecision(
            false, primary.Population, primary.Axis, canOverride,
            primary.Count, primary.Limit, remaining, overage,
            exceeded, listed, ordered.Count, ordered.Count - listed.Count, policy);
    }

    private static (DispatchConcurrencyMode Value, string Source) ResolveMode(
        DispatchConcurrencyDocument seed,
        DispatchConcurrencyDocument? globalOverrides,
        DispatchConcurrencyDocument? projectOverrides)
    {
        if (projectOverrides is { ModeSpecified: true, Mode: { } projectMode })
            return (projectMode, SourceProject);
        if (globalOverrides is { ModeSpecified: true, Mode: { } globalMode })
            return (globalMode, SourceGlobal);
        return (seed.Mode ?? DispatchConcurrencyMode.LegacyOpen, SourceDefault);
    }

    private static (int? Value, string Source) ResolveLimit(
        LimitField seed, LimitField? global, LimitField? project, bool allowNull)
    {
        if (project is { Specified: true } projectField && (allowNull || projectField.Value is not null))
            return (projectField.Value, SourceProject);
        if (global is { Specified: true } globalField && (allowNull || globalField.Value is not null))
            return (globalField.Value, SourceGlobal);
        return (seed.Value, SourceDefault);
    }

    private static void ValidateMode(JsonElement value, List<PolicyValidationError> errors)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Number)
        {
            errors.Add(new PolicyValidationError("mode", "mode must be LegacyOpen or SeparateQueues."));
            return;
        }

        if (value.ValueKind != JsonValueKind.String
            || !Enum.TryParse<DispatchConcurrencyMode>(value.GetString(), ignoreCase: false, out var mode)
            || !Enum.IsDefined(mode))
        {
            errors.Add(new PolicyValidationError("mode", "mode must be LegacyOpen or SeparateQueues."));
        }
    }

    private static void ValidateParallel(JsonElement value, string field, bool allowNull, List<PolicyValidationError> errors)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!allowNull)
                errors.Add(new PolicyValidationError(field, $"{field} must be an integer from {ParallelMin} to {ParallelMax}."));
            return;
        }

        if (!TryIntegral(value, out var number) || number < ParallelMin || number > ParallelMax)
            errors.Add(new PolicyValidationError(field, $"{field} must be an integer from {ParallelMin} to {ParallelMax}."));
    }

    private static void ValidateQueued(JsonElement value, string field, List<PolicyValidationError> errors)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;
        if (!TryIntegral(value, out var number) || number < QueuedMin || number > QueuedMax)
            errors.Add(new PolicyValidationError(field, $"{field} must be an integer from {QueuedMin} to {QueuedMax}, or null."));
    }

    private static void ValidateRoles(JsonElement value, List<PolicyValidationError> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new PolicyValidationError("roles", "roles must be an object."));
            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errors.Add(new PolicyValidationError("roles", $"Duplicate role '{property.Name}'."));
                continue;
            }

            if (IsNumericToken(property.Name)
                || !Enum.TryParse<AgentTaskRole>(property.Name, ignoreCase: false, out var role)
                || !Enum.IsDefined(role))
            {
                errors.Add(new PolicyValidationError("roles", $"Unknown role '{property.Name}'."));
                continue;
            }

            if (AgentTaskRoles.IsSpecialist(role))
            {
                errors.Add(new PolicyValidationError("roles", $"Specialist role '{property.Name}' cannot have a limit."));
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new PolicyValidationError($"roles.{property.Name}", "A role override must be an object."));
                continue;
            }

            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in property.Value.EnumerateObject())
            {
                if (!fields.Add(field.Name))
                {
                    errors.Add(new PolicyValidationError($"roles.{property.Name}.{field.Name}", "Duplicate field."));
                    continue;
                }

                if (field.NameEquals("maxParallel"))
                    ValidateParallel(field.Value, $"roles.{property.Name}.maxParallel", allowNull: true, errors);
                else if (field.NameEquals("maxQueued"))
                    ValidateQueued(field.Value, $"roles.{property.Name}.maxQueued", errors);
                else
                    errors.Add(new PolicyValidationError($"roles.{property.Name}.{field.Name}", $"Unknown field '{field.Name}'."));
            }
        }
    }

    private static bool TryIntegral(JsonElement value, out int number)
    {
        number = 0;
        if (value.ValueKind != JsonValueKind.Number)
            return false;
        if (!value.TryGetInt32(out number))
            return false;
        // Reject fractions that Truncate into an int. TryGetInt32 already rejects non-integers.
        var text = value.GetRawText();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
    }

    private static bool IsNumericToken(string name) =>
        name.Length > 0 && name.All(ch => char.IsAsciiDigit(ch) || ch == '-');

    private static string WriteCanonical(DispatchConcurrencyDocument document, bool includeAbsentRoles)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        if (document.ModeSpecified && document.Mode is { } mode)
            writer.WriteString("mode", mode.ToString());
        WriteLimit(writer, "maxParallel", document.MaxParallel);
        WriteLimit(writer, "maxQueued", document.MaxQueued);
        var roles = includeAbsentRoles
            ? OrdinaryRoles.Select(role =>
                document.Roles.TryGetValue(role, out var existing)
                    ? new KeyValuePair<AgentTaskRole, RoleDocument>(role, existing)
                    : new KeyValuePair<AgentTaskRole, RoleDocument>(role, new RoleDocument(new LimitField(false, null), new LimitField(false, null))))
            : document.Roles.OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal);
        var materialized = roles.ToList();
        if (materialized.Count > 0)
        {
            writer.WritePropertyName("roles");
            writer.WriteStartObject();
            foreach (var pair in materialized)
            {
                writer.WritePropertyName(pair.Key.ToString());
                writer.WriteStartObject();
                WriteLimit(writer, "maxParallel", pair.Value.MaxParallel);
                WriteLimit(writer, "maxQueued", pair.Value.MaxQueued);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteLimit(Utf8JsonWriter writer, string name, LimitField field)
    {
        if (!field.Specified)
            return;
        writer.WritePropertyName(name);
        if (field.Value is int number)
            writer.WriteNumberValue(number);
        else
            writer.WriteNullValue();
    }
}

public enum DispatchConcurrencyMode
{
    LegacyOpen = 0,
    SeparateQueues = 1,
}

public readonly record struct LimitField(bool Specified, int? Value);

public sealed record RoleDocument(LimitField MaxParallel, LimitField MaxQueued);

public sealed record DispatchConcurrencyDocument(
    bool ModeSpecified,
    DispatchConcurrencyMode? Mode,
    LimitField MaxParallel,
    LimitField MaxQueued,
    IReadOnlyDictionary<AgentTaskRole, RoleDocument> Roles,
    DateTime? ImportedAt);

public readonly record struct PolicyValidationError(string Field, string Message);

public sealed record TaskPopulationRow(
    Guid Id,
    Guid? ProjectId,
    AgentTaskRole Role,
    AgentTaskStatus Status,
    DateTime CreatedAt,
    bool CapacityWaitRetained,
    string Title,
    string? Stuck = null);

public sealed record RolePopulation(int Open, int Parallel, int Queued);

public sealed record PopulationSnapshot(
    int Open,
    int Parallel,
    int Queued,
    IReadOnlyDictionary<AgentTaskRole, RolePopulation> Roles,
    IReadOnlyList<TaskPopulationRow> OpenRows,
    IReadOnlyList<TaskPopulationRow> ParallelRows,
    IReadOnlyList<TaskPopulationRow> QueuedRows);

public sealed record ResolvedRole(
    AgentTaskRole Role,
    int? MaxParallel,
    string ParallelSource,
    int? MaxQueued,
    string QueuedSource);

public sealed record EffectivePolicy(
    DispatchConcurrencyMode Mode,
    string ModeSource,
    int MaxParallel,
    string ParallelSource,
    int? MaxQueued,
    string QueuedSource,
    IReadOnlyList<ResolvedRole> Roles,
    long GlobalRevision,
    long ProjectRevision)
{
    public ResolvedRole Role(AgentTaskRole role) =>
        Roles.Single(item => item.Role == role);

    public int? CombinedParallel(AgentTaskRole role)
    {
        var roleLimit = Role(role).MaxParallel;
        if (roleLimit is null)
            return MaxParallel;
        return Math.Min(MaxParallel, roleLimit.Value);
    }
}

public sealed record DispatchConstraint(
    string Population,
    string Axis,
    string? Role,
    int Count,
    int Limit,
    string Source);

public sealed record DispatchDecision(
    bool Admit,
    string? Population,
    string? Axis,
    bool CanOverride,
    int Count,
    int Limit,
    int? Remaining,
    int Overage,
    IReadOnlyList<DispatchConstraint> Exceeded,
    IReadOnlyList<Guid> ListedOccupantIds,
    int TotalOccupants,
    int Omitted,
    EffectivePolicy Policy);
