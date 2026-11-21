using System.IO.Abstractions;

namespace Antiphon.IO.WriteBehind;

/// <summary>
/// The rename that lands an atomic replace: a temporary file moved over its target.
/// On Windows another process (an on-access scanner, the search indexer) that has just opened the target or the
/// temporary file without delete sharing makes the rename fail with access denied or a sharing violation for a
/// moment. Those two failures are retried briefly (at most about 0.6 s in all); any other failure, and the last
/// attempt's, is the rename's failure. Elsewhere the rename is not retried.
/// </summary>
public static class AtomicRename
{
    private const int Attempts = 6;

    /// <summary>Moves <paramref name="temp"/> over <paramref name="path"/> through <paramref name="file"/>.</summary>
    public static void MoveOver(IFile file, string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                file.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < Attempts && OperatingSystem.IsWindows() && Transient(ex))
            {
                Thread.Sleep(10 << attempt);
            }
        }
    }

    /// <summary>Moves <paramref name="temp"/> over <paramref name="path"/> on the real file system.</summary>
    public static void MoveOver(string temp, string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < Attempts && OperatingSystem.IsWindows() && Transient(ex))
            {
                Thread.Sleep(10 << attempt);
            }
        }
    }

    // ERROR_ACCESS_DENIED (5) surfaces as UnauthorizedAccessException; ERROR_SHARING_VIOLATION (32) and
    // ERROR_LOCK_VIOLATION (33) as IOException.
    private static bool Transient(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException io && (io.HResult & 0xFFFF) is 32 or 33);
}
