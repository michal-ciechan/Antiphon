using System.Reflection;
using Microsoft.Build.Evaluation;

namespace Antiphon.LandGraphAudit;

/// <summary>
/// Plan D-1 spike (V-4): the members later slices rely on exist in the MSBuild the Locator loaded.
/// Looked up by name at run time, so a compile-time package reference cannot vouch for them.
/// </summary>
internal static class ApiProbe
{
    public static int Run(string sdkVersion, TextWriter output)
    {
        var assembly = typeof(Project).Assembly;
        output.WriteLine($"sdk: {sdkVersion}");
        output.WriteLine($"msbuild: {assembly.Location}");

        var missing = 0;
        void Check(string type, string? member, Func<Type, bool> exists)
        {
            var name = member is null ? type : $"{type}.{member}";
            var found = assembly.GetType(type) is { } t && exists(t);
            output.WriteLine(found ? $"api: {name} ok" : $"api-missing: {name}");
            if (!found) missing++;
        }

        bool Property(Type t, string name) => t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not null;

        Check("Microsoft.Build.Evaluation.Project", "GetAllGlobs()",
            t => t.GetMethod("GetAllGlobs", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is not null);
        Check("Microsoft.Build.Evaluation.Project", "Imports", t => Property(t, "Imports"));
        Check("Microsoft.Build.Evaluation.Project", "ImportsIncludingDuplicates", t => Property(t, "ImportsIncludingDuplicates"));
        Check("Microsoft.Build.Evaluation.ProjectProperty", "IsEnvironmentProperty", t => Property(t, "IsEnvironmentProperty"));
        Check("Microsoft.Build.Evaluation.GlobResult", "MsBuildGlob", t => Property(t, "MsBuildGlob"));
        Check("Microsoft.Build.Globbing.MSBuildGlob", "FixedDirectoryPart", t => Property(t, "FixedDirectoryPart"));
        Check("Microsoft.Build.Globbing.MSBuildGlob", "WildcardDirectoryPart", t => Property(t, "WildcardDirectoryPart"));
        Check("Microsoft.Build.Globbing.MSBuildGlob", "FilenamePart", t => Property(t, "FilenamePart"));
        Check("Microsoft.Build.FileSystem.MSBuildFileSystemBase", null, t => t.IsAbstract && !t.IsSealed);
        Check("Microsoft.Build.Evaluation.Context.EvaluationContext", "Create(SharingPolicy, MSBuildFileSystemBase)",
            t => t.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "Create"
                && m.GetParameters().Select(p => p.ParameterType.Name).SequenceEqual(["SharingPolicy", "MSBuildFileSystemBase"])));
        Check("Microsoft.Build.Definition.ProjectOptions", "EvaluationContext", t => Property(t, "EvaluationContext"));

        return missing == 0 ? Program.Approved : Program.EvaluationFailed;
    }
}
