namespace Antiphon.Tests.TestHelpers;

internal static class EffortFixtureComposer
{
    internal static string Screen(string composer, string effort = "xhigh") =>
        $"Fable 5.1 with {effort} effort\n> {composer}\n? for shortcuts";
}
