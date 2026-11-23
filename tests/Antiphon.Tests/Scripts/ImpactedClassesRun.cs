using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The impacted-classes selector in this process: the antiphon-scripts impacted-classes command line on a host whose
/// stdout and stderr are captured. The checkout given is the selector's source root (its default repository and the
/// release evidence's algorithm source) and the directory a relative --repo or --file-list resolves against. Until
/// 2026-11-22 the tests ran scripts/impacted-classes.ps1 under pwsh (DerivedSelectorReplay kept one host per class).
/// </summary>
internal static class ImpactedClassesRun
{
    /// <summary>
    /// ImpactedClasses.SelectorFiles, as literals the derived impact index reads: through this helper (one hop) a change
    /// to the selector's sources selects every class that runs the selector here (ImpactedClassesCensusTests guards
    /// the list).
    /// </summary>
    public static readonly string[] SelectorSources =
    [
        "scripts/lib/Antiphon.Nightly/ImpactedClasses.cs",
        "scripts/lib/Antiphon.Nightly/ImpactedClassesInputs.cs",
        "scripts/lib/Antiphon.Nightly/ReleaseImpactEvidence.cs",
    ];

    /// <summary>One run: the exit code, stdout, and stdout followed by stderr.</summary>
    public static (int Exit, string Stdout, string Text) Select(string sourceRoot, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit;
        try
        {
            var options = ImpactedClassesOptions.Parse(args, sourceRoot, sourceRoot);
            exit = ImpactedClasses.Run(options, NightlyHost.Create(null, new RecordingLauncher(), output, error, null, null, null, null));
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine("usage: antiphon-scripts impacted-classes: " + e.Message);
            exit = 2;
        }

        var stdout = output.ToString();
        return (exit, stdout, stdout + error);
    }

    /// <summary>The same run on the thread pool, for the async test bodies.</summary>
    public static Task<(int Exit, string Stdout, string Text)> SelectAsync(string sourceRoot, params string[] args) =>
        Task.Run(() => Select(sourceRoot, args));
}
