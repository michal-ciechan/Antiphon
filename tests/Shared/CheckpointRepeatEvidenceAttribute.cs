using System.Text.Json;
using System.Text.RegularExpressions;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Antiphon.Checkpoints;

/// <summary>Emits native repeat identity into each test's captured TRX output.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class RepeatEvidenceAttribute(int requested) : Attribute, ITestStartEventReceiver, ITestEndEventReceiver
{
    private const string StateKey = "antiphon.checkpoint.repeat.identity";
    private static readonly Regex Tail = new(@"^(?<key>.+\.\d+\.\d+)\.(?<ordinal>\d+)(?<inherit>_inherited[1-9]\d*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async ValueTask OnTestStart(TestContext context)
    {
        var details = context.Metadata.TestDetails;
        var id = details.TestId;
        var match = Tail.Match(id);
        var repeats = details.GetAttributes<RepeatAttribute>().ToArray();
        if (!match.Success || !int.TryParse(match.Groups["ordinal"].Value, out var ordinal)
            || ordinal < 0 || ordinal >= requested || repeats.Length != 1 || repeats[0].Times != requested - 1)
        {
            await context.OutputWriter.WriteLineAsync("C885_REPEAT_INVALID unsupported-or-conflicting-native-id");
            return;
        }

        var key = match.Groups["key"].Value + match.Groups["inherit"].Value;
        var data = new RepeatMarker(1, requested, Environment.GetEnvironmentVariable("ANTIPHON_CHECKPOINT_NONCE") ?? "",
            id, ordinal, key, details.ClassType.FullName ?? details.ClassType.Name, details.MethodName, Environment.ProcessId);
        context.StateBag[StateKey] = data;
        await context.OutputWriter.WriteLineAsync("C885_REPEAT_START " + JsonSerializer.Serialize(data));
    }

    public async ValueTask OnTestEnd(TestContext context)
    {
        if (context.StateBag[StateKey] is RepeatMarker marker)
            await context.OutputWriter.WriteLineAsync("C885_REPEAT_END " + JsonSerializer.Serialize(marker));
        else
            await context.OutputWriter.WriteLineAsync("C885_REPEAT_INVALID missing-start");
    }

    private sealed record RepeatMarker(int Version, int Requested, string Nonce, string NativeId,
        int Ordinal, string CaseKey, string Class, string Method, int HostPid);
}
