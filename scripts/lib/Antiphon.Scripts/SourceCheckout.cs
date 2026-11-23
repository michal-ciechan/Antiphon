// The checkout a command's defaults come from: the one this executable was built in (scripts/antiphon builds it under
// <checkout>/bin-launcher), else the one the current directory is in. ASCII only.
using System;
using System.IO;

namespace Antiphon.Scripts
{
    internal static class SourceCheckout
    {
        /// <summary>The checkout's root directory, or null when neither start is inside one.</summary>
        internal static string Root()
        {
            foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (File.Exists(Path.Combine(directory.FullName, "scripts", "lib", "Antiphon.Nightly", "Antiphon.Nightly.csproj"))) return directory.FullName;
            }
            return null;
        }
    }
}
