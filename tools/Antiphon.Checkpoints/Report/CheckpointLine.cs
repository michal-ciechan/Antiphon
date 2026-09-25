namespace Antiphon.Checkpoints;

public sealed class CheckpointLineModel
{
    public string Name { get; init; } = "";
    public string Commit { get; init; } = new string('0', 40);
    public string Build { get; init; } = "ok";
    public string Filter { get; init; } = "";
    public string? Executed { get; init; }
    public string? Passed { get; init; }
    public string? Failed { get; init; }
    public string? Skipped { get; init; }
    public string? Trx { get; init; }
    public string Slot { get; init; } = "unavailable";
    public int WaitedSeconds { get; init; }
    public int Reruns { get; init; }
    public int? ExitCode { get; init; }
    public string? Timeout { get; init; }
    public bool Command { get; init; }
}

public static class CheckpointLine
{
    public static string Format(CheckpointLineModel model)
    {
        var executed = model.Executed ?? (model.Command || model.Timeout is not null ? "n/a" : "0");
        var passed = model.Passed ?? (model.Command || model.Timeout is not null ? "n/a" : "0");
        var failed = model.Failed ?? (model.Command || model.Timeout is not null ? "n/a" : "0");
        var skipped = model.Skipped ?? (model.Command || model.Timeout is not null ? "n/a" : "0");
        var trx = model.Trx ?? "n/a";
        var line =
            $"CHECKPOINT {model.Name} commit={model.Commit} build={model.Build} filter={model.Filter} " +
            $"executed={executed} passed={passed} failed={failed} skipped={skipped} trx={trx}";
        if (model.Timeout is not null)
            line += " timeout=" + model.Timeout;
        if (model.Command && model.ExitCode is int code)
            line += " exit=" + code;
        line += $" slot={model.Slot} waited={model.WaitedSeconds}s";
        if (model.Reruns > 0)
            line += " reruns=" + model.Reruns;
        return line;
    }
}
