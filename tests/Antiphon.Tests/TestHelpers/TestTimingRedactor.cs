using System.Runtime.CompilerServices;
using Antiphon.Server.Application.Services;

namespace Antiphon.Tests.TestHelpers;

internal static class TestTimingRedactor
{
    [ModuleInitializer]
    internal static void Install() =>
        TestTiming.FreeTextRedactor = text => new DiagnosticsRedactor(includePaths: false).Redact(text);
}
