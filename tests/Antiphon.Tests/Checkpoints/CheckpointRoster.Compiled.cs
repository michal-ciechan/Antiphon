using System.Reflection;
using Antiphon.TestSupport;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

internal static partial class CheckpointRoster
{
    // Reflection inventory of the compiled cases a wildcard selection runs, independent
    // of TUnit's own selection: one per [Arguments] row, else one per [Test] method.
    public static IReadOnlyList<(string ClassName, string Method)> CompiledCases(Assembly assembly, string? ns = null) =>
        assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && (ns is null || type.Namespace == ns))
            .SelectMany(type => TestClassificationMetadata.GetTestMethods(type)
                .Where(method => !IsExplicit(type, method.Name))
                .SelectMany(method => Enumerable.Repeat((type.FullName!, method.Name),
                    Math.Max(1, method.GetCustomAttributes(typeof(ArgumentsAttribute), inherit: true).Length))))
            .OrderBy(test => test.Item1, StringComparer.Ordinal).ThenBy(test => test.Item2, StringComparer.Ordinal)
            .ToArray();
}
