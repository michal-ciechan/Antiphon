namespace Antiphon.Server.Application.Services;

/// <summary>
/// One line per changed fact. Occupancy and observation timestamps are not facts.
/// At most six lines, then <c>+N more</c>. Each line is at most 120 characters and the
/// whole text is at most 700.
/// </summary>
public static class OrchestratorInstructionsDelta
{
    public const int MaxLines = 6;
    public const int MaxLineChars = 120;
    public const int MaxBodyChars = 700;

    public static string Format(
        OrchestratorInstructionsSnapshot? before,
        OrchestratorInstructionsSnapshot after)
    {
        ArgumentNullException.ThrowIfNull(after);
        var lines = new List<string>();
        DiffCaps(lines, before?.Caps, after.Caps);
        DiffRunners(lines, before?.Runners ?? [], after.Runners);
        DiffDefaults(lines, before?.Defaults, after.Defaults);
        DiffHolds(lines, before?.Holds ?? [], after.Holds);
        DiffPins(lines, before?.Pins ?? [], after.Pins);
        DiffLevels(lines, before?.Levels ?? [], after.Levels);
        DiffStanding(lines, before?.StandingLines ?? [], after.StandingLines);
        return Cap(lines);
    }

    private static void DiffCaps(
        List<string> lines,
        PipelineCapsSection? before,
        PipelineCapsSection after)
    {
        DiffScalar(lines, "caps", "max concurrent tasks",
            before is null ? null : $"{before.EffectiveMaxConcurrentTasks} ({before.MaxConcurrentSource})",
            $"{after.EffectiveMaxConcurrentTasks} ({after.MaxConcurrentSource})");
        DiffScalar(lines, "caps", "max open tasks",
            before?.MaxOpenTasks.ToString(), after.MaxOpenTasks.ToString());
        DiffScalar(lines, "caps", "default worker workspace",
            before?.DefaultWorkerWorkspace, after.DefaultWorkerWorkspace);
        DiffScalar(lines, "caps", "min orchestrator level",
            before?.MinOrchestratorLevel, after.MinOrchestratorLevel);

        var beforeRoles = before?.Roles ?? [];
        var beforeByRole = beforeRoles.ToDictionary(r => r.Role, StringComparer.Ordinal);
        var afterByRole = after.Roles.ToDictionary(r => r.Role, StringComparer.Ordinal);
        foreach (var role in beforeRoles.Select(r => r.Role).Concat(after.Roles.Select(r => r.Role)).Distinct())
        {
            beforeByRole.TryGetValue(role, out var oldRole);
            afterByRole.TryGetValue(role, out var newRole);
            if (oldRole is null && newRole is not null)
            {
                lines.Add($"caps: {role} added");
                continue;
            }

            if (oldRole is not null && newRole is null)
            {
                lines.Add($"caps: {role} removed");
                continue;
            }

            if (oldRole is null || newRole is null)
                continue;
            DiffScalar(lines, "caps", $"{role} recommended in-flight",
                FormatInFlight(oldRole.RecommendedInFlight), FormatInFlight(newRole.RecommendedInFlight));
            DiffScalar(lines, "caps", $"{role} level", oldRole.Level, newRole.Level);
            DiffScalar(lines, "caps", $"{role} escalate-to", oldRole.EscalateTo ?? "none", newRole.EscalateTo ?? "none");
            DiffScalar(lines, "caps", $"{role} kind", oldRole.Kind ?? "none", newRole.Kind ?? "none");
        }
    }

    private static void DiffRunners(
        List<string> lines,
        IReadOnlyList<RunnerInstructionRow> before,
        IReadOnlyList<RunnerInstructionRow> after)
    {
        var beforeById = before.ToDictionary(r => r.RunnerId, StringComparer.Ordinal);
        var afterById = after.ToDictionary(r => r.RunnerId, StringComparer.Ordinal);
        foreach (var id in before.Select(r => r.RunnerId).Concat(after.Select(r => r.RunnerId)).Distinct())
        {
            beforeById.TryGetValue(id, out var oldRow);
            afterById.TryGetValue(id, out var newRow);
            if (oldRow is null && newRow is not null)
            {
                lines.Add($"runners: {id} added");
                continue;
            }

            if (oldRow is not null && newRow is null)
            {
                lines.Add($"runners: {id} removed");
                continue;
            }

            if (oldRow is null || newRow is null)
                continue;
            DiffScalar(lines, "runners", $"{id} platform", oldRow.Platform ?? "unknown", newRow.Platform ?? "unknown");
            DiffScalar(lines, "runners", $"{id} dispatch-eligible", Yes(oldRow.DispatchEligible), Yes(newRow.DispatchEligible));
            DiffScalar(lines, "runners", $"{id} declared capacity", Opt(oldRow.DeclaredCapacity), Opt(newRow.DeclaredCapacity));
            DiffScalar(lines, "runners", $"{id} effective budget",
                $"{Opt(oldRow.EffectiveBudget)} ({oldRow.BudgetSource ?? "none"})",
                $"{Opt(newRow.EffectiveBudget)} ({newRow.BudgetSource ?? "none"})");
            DiffScalar(lines, "runners", $"{id} draining", Yes(oldRow.Draining), Yes(newRow.Draining));
            DiffScalar(lines, "runners", $"{id} retired", Yes(oldRow.Retired), Yes(newRow.Retired));
            DiffScalar(lines, "runners", $"{id} features", Join(oldRow.Features), Join(newRow.Features));
        }
    }

    private static void DiffDefaults(
        List<string> lines,
        RunnerDefaultsSection? before,
        RunnerDefaultsSection after)
    {
        DiffScalar(lines, "defaults", "revision", before?.Revision.ToString(), after.Revision.ToString());
        DiffScalar(lines, "defaults", "provenance", before?.Provenance ?? "none", after.Provenance ?? "none");
        DiffScalar(lines, "defaults", "global", before?.GlobalRunnerId ?? "none", after.GlobalRunnerId ?? "none");
        DiffScalar(lines, "defaults", "reason", before?.LastReason ?? "none", after.LastReason ?? "none");
        var beforeKinds = (before?.KindDefaults ?? []).ToDictionary(k => k.Kind, StringComparer.Ordinal);
        var afterKinds = after.KindDefaults.ToDictionary(k => k.Kind, StringComparer.Ordinal);
        foreach (var kind in beforeKinds.Keys.Concat(afterKinds.Keys).Distinct())
        {
            beforeKinds.TryGetValue(kind, out var oldKind);
            afterKinds.TryGetValue(kind, out var newKind);
            if (oldKind is null && newKind is not null)
                lines.Add($"defaults: {kind} added");
            else if (oldKind is not null && newKind is null)
                lines.Add($"defaults: {kind} removed");
            else if (oldKind is not null && newKind is not null)
                DiffScalar(lines, "defaults", kind, oldKind.RunnerId ?? "none", newKind.RunnerId ?? "none");
        }
    }

    private static void DiffHolds(
        List<string> lines,
        IReadOnlyList<HoldInstructionRow> before,
        IReadOnlyList<HoldInstructionRow> after)
    {
        var beforeBy = before.ToDictionary(Key, StringComparer.Ordinal);
        var afterBy = after.ToDictionary(Key, StringComparer.Ordinal);
        foreach (var key in before.Select(Key).Concat(after.Select(Key)).Distinct())
        {
            beforeBy.TryGetValue(key, out var oldHold);
            afterBy.TryGetValue(key, out var newHold);
            if (oldHold is null && newHold is not null)
                lines.Add($"holds: {key} added");
            else if (oldHold is not null && newHold is null)
                lines.Add($"holds: {key} removed");
            else if (oldHold is not null && newHold is not null)
            {
                DiffScalar(lines, "holds", $"{key} source", oldHold.Source, newHold.Source);
                DiffScalar(lines, "holds", $"{key} until", oldHold.Until ?? "none", newHold.Until ?? "none");
                DiffScalar(lines, "holds", $"{key} reason", oldHold.Reason ?? "none", newHold.Reason ?? "none");
            }
        }
    }

    private static void DiffPins(
        List<string> lines,
        IReadOnlyList<PinInstructionRow> before,
        IReadOnlyList<PinInstructionRow> after)
    {
        var beforeBy = before.ToDictionary(PinKey, StringComparer.Ordinal);
        var afterBy = after.ToDictionary(PinKey, StringComparer.Ordinal);
        foreach (var key in before.Select(PinKey).Concat(after.Select(PinKey)).Distinct())
        {
            beforeBy.TryGetValue(key, out var oldPin);
            afterBy.TryGetValue(key, out var newPin);
            if (oldPin is null && newPin is not null)
                lines.Add($"pins: {key} added");
            else if (oldPin is not null && newPin is null)
                lines.Add($"pins: {key} removed");
            else if (oldPin is not null && newPin is not null)
            {
                DiffScalar(lines, "pins", $"{key} candidates", Join(oldPin.Candidates), Join(newPin.Candidates));
                DiffScalar(lines, "pins", $"{key} forbidden", Join(oldPin.ForbiddenAliases), Join(newPin.ForbiddenAliases));
                DiffScalar(lines, "pins", $"{key} strength", oldPin.Strength ?? "none", newPin.Strength ?? "none");
                DiffScalar(lines, "pins", $"{key} reason", oldPin.Reason ?? "none", newPin.Reason ?? "none");
            }
        }
    }

    private static void DiffLevels(
        List<string> lines,
        IReadOnlyList<LevelInstructionRow> before,
        IReadOnlyList<LevelInstructionRow> after)
    {
        var beforeBy = before.ToDictionary(l => l.Kind + "/" + l.Level, StringComparer.Ordinal);
        var afterBy = after.ToDictionary(l => l.Kind + "/" + l.Level, StringComparer.Ordinal);
        foreach (var key in beforeBy.Keys.Concat(afterBy.Keys).Distinct())
        {
            beforeBy.TryGetValue(key, out var oldLevel);
            afterBy.TryGetValue(key, out var newLevel);
            if (oldLevel is null && newLevel is not null)
                lines.Add($"levels: {key} added");
            else if (oldLevel is not null && newLevel is null)
                lines.Add($"levels: {key} removed");
            else if (oldLevel is not null && newLevel is not null)
                DiffScalar(lines, "levels", key, oldLevel.Alias, newLevel.Alias);
        }
    }

    private static void DiffStanding(
        List<string> lines,
        IReadOnlyList<string> before,
        IReadOnlyList<string> after)
    {
        var removed = before.Where(line => !after.Contains(line, StringComparer.Ordinal)).ToList();
        var added = after.Where(line => !before.Contains(line, StringComparer.Ordinal)).ToList();
        foreach (var line in removed)
            lines.Add($"standing: {line} removed");
        foreach (var line in added)
            lines.Add($"standing: {line} added");
    }

    private static void DiffScalar(List<string> lines, string section, string key, string? oldValue, string? newValue)
    {
        if (oldValue is null && newValue is null)
            return;
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
            return;
        if (oldValue is null)
            lines.Add($"{section}: {key} added");
        else if (newValue is null)
            lines.Add($"{section}: {key} removed");
        else
            lines.Add($"{section}: {key} {oldValue} → {newValue}");
    }

    private static string Cap(List<string> lines)
    {
        if (lines.Count == 0)
            return "";

        var take = Math.Min(MaxLines, lines.Count);
        while (true)
        {
            var visible = lines.Take(take).Select(FitLine).ToList();
            if (lines.Count > take)
                visible.Add($"+{lines.Count - take} more");
            var body = string.Join("\n", visible);
            if (body.Length <= MaxBodyChars || take == 0)
                return body;
            take--;
        }
    }

    private static string FitLine(string line) =>
        line.Length <= MaxLineChars ? line : line[..(MaxLineChars - 1)] + "…";

    private static string Key(HoldInstructionRow hold) => hold.Kind + "/" + hold.Alias;

    private static string PinKey(PinInstructionRow pin) =>
        pin.StageWide
            ? "stage-wide/" + pin.Role
            : (pin.BoardName ?? "board") + "/" + (pin.CardIdentifier ?? "card") + "/" + pin.Role;

    private static string FormatInFlight(int? value) => value?.ToString() ?? "unbounded";

    private static string Yes(bool value) => value ? "yes" : "no";

    private static string Opt(int? value) => value?.ToString() ?? "none";

    private static string Join(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);
}
