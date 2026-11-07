using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Antiphon.IO.WriteBehind;

/// <summary>
/// <see cref="IFile"/> for <see cref="WriteBehindFileSystem"/>: whole-file writes are queued, whole-file reads
/// answer from the queue, everything else on a path waits for that path's queued write and then goes to the
/// inner file system.
/// </summary>
internal sealed class WriteBehindFile : IFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly IFileSystem owner;
    private readonly IFile inner;
    private readonly WriteBehindQueue queue;

    public WriteBehindFile(IFileSystem owner, IFileSystem inner, WriteBehindQueue queue)
    {
        this.owner = owner;
        this.inner = inner.File;
        this.queue = queue;
    }

    public IFileSystem FileSystem => owner;

    // ---- Queued writes -------------------------------------------------------------------------------------

    private void Queue(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        // What File.WriteAll* would refuse now, refused now rather than on the writer thread.
        string full = owner.Path.GetFullPath(path);
        string? dir = owner.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir) && !owner.Directory.Exists(dir)) throw new DirectoryNotFoundException("Could not find a part of the path '" + full + "'.");
        if (owner.Directory.Exists(full)) throw new UnauthorizedAccessException("Access to the path '" + full + "' is denied.");
        queue.Enqueue(full, content);
    }

    // As File.WriteAllText: the encoding's preamble (even for empty text), then the text.
    private static byte[] Encode(ReadOnlySpan<char> text, Encoding? encoding)
    {
        encoding ??= Utf8NoBom;
        ReadOnlySpan<byte> preamble = encoding.Preamble;
        var bytes = new byte[preamble.Length + encoding.GetByteCount(text)];
        preamble.CopyTo(bytes);
        encoding.GetBytes(text, bytes.AsSpan(preamble.Length));
        return bytes;
    }

    // As File.WriteAllLines / StreamWriter.WriteLine: each line and Environment.NewLine; the preamble always.
    private static byte[] EncodeLines(IEnumerable<string> lines, Encoding? encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        encoding ??= Utf8NoBom;
        var text = new StringBuilder();
        foreach (string line in lines) text.Append(line).Append(Environment.NewLine);
        ReadOnlySpan<byte> preamble = encoding.Preamble;
        string s = text.ToString();
        var bytes = new byte[preamble.Length + encoding.GetByteCount(s)];
        preamble.CopyTo(bytes);
        encoding.GetBytes(s, 0, s.Length, bytes, preamble.Length);
        return bytes;
    }

    private static Task Completed(CancellationToken token, Action action)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled(token);
        try { action(); return Task.CompletedTask; }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    private static Task<T> Completed<T>(CancellationToken token, Func<T> func)
    {
        if (token.IsCancellationRequested) return Task.FromCanceled<T>(token);
        try { return Task.FromResult(func()); }
        catch (Exception ex) { return Task.FromException<T>(ex); }
    }

    public void WriteAllBytes(string path, byte[] bytes) { ArgumentNullException.ThrowIfNull(bytes); Queue(path, (byte[])bytes.Clone()); }
    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes) => Queue(path, bytes.ToArray());
    public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllBytes(path, bytes));
    public Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllBytes(path, bytes.Span));

    public void WriteAllText(string path, string? contents) => Queue(path, Encode(contents, null));
    public void WriteAllText(string path, string? contents, Encoding encoding) => Queue(path, Encode(contents, encoding));
    public void WriteAllText(string path, ReadOnlySpan<char> contents) => Queue(path, Encode(contents, null));
    public void WriteAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => Queue(path, Encode(contents, encoding));
    public Task WriteAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllText(path, contents));
    public Task WriteAllTextAsync(string path, string? contents, Encoding encoding, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllText(path, contents, encoding));
    public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllText(path, contents.Span));
    public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllText(path, contents.Span, encoding));

    public void WriteAllLines(string path, string[] contents) => Queue(path, EncodeLines(contents, null));
    public void WriteAllLines(string path, IEnumerable<string> contents) => Queue(path, EncodeLines(contents, null));
    public void WriteAllLines(string path, string[] contents, Encoding encoding) => Queue(path, EncodeLines(contents, encoding));
    public void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding) => Queue(path, EncodeLines(contents, encoding));
    public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllLines(path, contents));
    public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default) => Completed(cancellationToken, () => WriteAllLines(path, contents, encoding));

    // ---- Reads answered from the queue ---------------------------------------------------------------------

    private static StreamReader Reader(byte[] content, Encoding? encoding) =>
        new(new MemoryStream(content, writable: false), encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

    public bool Exists(string? path) => (!string.IsNullOrEmpty(path) && queue.TryGet(path, out _)) || inner.Exists(path);

    public byte[] ReadAllBytes(string path) => queue.TryGet(path, out var c) ? (byte[])c.Clone() : inner.ReadAllBytes(path);
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out _) ? Completed(cancellationToken, () => ReadAllBytes(path)) : inner.ReadAllBytesAsync(path, cancellationToken);

    public string ReadAllText(string path) => ReadAllText(path, null);
    public string ReadAllText(string path, Encoding? encoding)
    {
        if (!queue.TryGet(path, out var c)) return encoding is null ? inner.ReadAllText(path) : inner.ReadAllText(path, encoding);
        using var reader = Reader(c, encoding);
        return reader.ReadToEnd();
    }
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out _) ? Completed(cancellationToken, () => ReadAllText(path)) : inner.ReadAllTextAsync(path, cancellationToken);
    public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out _) ? Completed(cancellationToken, () => ReadAllText(path, encoding)) : inner.ReadAllTextAsync(path, encoding, cancellationToken);

    private static string[] Lines(byte[] content, Encoding? encoding)
    {
        using var reader = Reader(content, encoding);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null) lines.Add(line);
        return lines.ToArray();
    }

    public string[] ReadAllLines(string path) => queue.TryGet(path, out var c) ? Lines(c, null) : inner.ReadAllLines(path);
    public string[] ReadAllLines(string path, Encoding encoding) => queue.TryGet(path, out var c) ? Lines(c, encoding) : inner.ReadAllLines(path, encoding);
    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out _) ? Completed(cancellationToken, () => ReadAllLines(path)) : inner.ReadAllLinesAsync(path, cancellationToken);
    public Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out _) ? Completed(cancellationToken, () => ReadAllLines(path, encoding)) : inner.ReadAllLinesAsync(path, encoding, cancellationToken);

    public IEnumerable<string> ReadLines(string path) => queue.TryGet(path, out var c) ? Lines(c, null) : inner.ReadLines(path);
    public IEnumerable<string> ReadLines(string path, Encoding encoding) => queue.TryGet(path, out var c) ? Lines(c, encoding) : inner.ReadLines(path, encoding);
    public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out var c) ? AsAsync(Lines(c, null), cancellationToken) : inner.ReadLinesAsync(path, cancellationToken);
    public IAsyncEnumerable<string> ReadLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken = default) =>
        queue.TryGet(path, out var c) ? AsAsync(Lines(c, encoding), cancellationToken) : inner.ReadLinesAsync(path, encoding, cancellationToken);

    private static async IAsyncEnumerable<string> AsAsync(string[] lines, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (string line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return line;
        }
        await Task.CompletedTask;
    }

    public StreamReader OpenText(string path) => queue.TryGet(path, out var c) ? Reader(c, null) : inner.OpenText(path);

    // ---- Deletes drop the queued write ---------------------------------------------------------------------

    public void Delete(string path)
    {
        queue.Cancel(path);
        inner.Delete(path);
    }

    // ---- Everything else: the path's queued write lands first ----------------------------------------------

    private string Landed(string path)
    {
        if (!string.IsNullOrEmpty(path)) queue.Flush(path);
        return path;
    }

    public void AppendAllBytes(string path, byte[] bytes) => inner.AppendAllBytes(Landed(path), bytes);
    public void AppendAllBytes(string path, ReadOnlySpan<byte> bytes) => inner.AppendAllBytes(Landed(path), bytes);
    public Task AppendAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default) => inner.AppendAllBytesAsync(Landed(path), bytes, cancellationToken);
    public Task AppendAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => inner.AppendAllBytesAsync(Landed(path), bytes, cancellationToken);
    public void AppendAllLines(string path, IEnumerable<string> contents) => inner.AppendAllLines(Landed(path), contents);
    public void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding) => inner.AppendAllLines(Landed(path), contents, encoding);
    public Task AppendAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken = default) => inner.AppendAllLinesAsync(Landed(path), contents, cancellationToken);
    public Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken = default) => inner.AppendAllLinesAsync(Landed(path), contents, encoding, cancellationToken);
    public void AppendAllText(string path, string? contents) => inner.AppendAllText(Landed(path), contents);
    public void AppendAllText(string path, string? contents, Encoding encoding) => inner.AppendAllText(Landed(path), contents, encoding);
    public void AppendAllText(string path, ReadOnlySpan<char> contents) => inner.AppendAllText(Landed(path), contents);
    public void AppendAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => inner.AppendAllText(Landed(path), contents, encoding);
    public Task AppendAllTextAsync(string path, string? contents, CancellationToken cancellationToken = default) => inner.AppendAllTextAsync(Landed(path), contents, cancellationToken);
    public Task AppendAllTextAsync(string path, string? contents, Encoding encoding, CancellationToken cancellationToken = default) => inner.AppendAllTextAsync(Landed(path), contents, encoding, cancellationToken);
    public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken = default) => inner.AppendAllTextAsync(Landed(path), contents, cancellationToken);
    public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken = default) => inner.AppendAllTextAsync(Landed(path), contents, encoding, cancellationToken);
    public StreamWriter AppendText(string path) => inner.AppendText(Landed(path));

    public void Copy(string sourceFileName, string destFileName) => inner.Copy(Landed(sourceFileName), Landed(destFileName));
    public void Copy(string sourceFileName, string destFileName, bool overwrite) => inner.Copy(Landed(sourceFileName), Landed(destFileName), overwrite);
    public void Move(string sourceFileName, string destFileName) => inner.Move(Landed(sourceFileName), Landed(destFileName));
    public void Move(string sourceFileName, string destFileName, bool overwrite) => inner.Move(Landed(sourceFileName), Landed(destFileName), overwrite);
    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName) =>
        inner.Replace(Landed(sourceFileName), Landed(destinationFileName), destinationBackupFileName is null ? null : Landed(destinationBackupFileName));
    public void Replace(string sourceFileName, string destinationFileName, string? destinationBackupFileName, bool ignoreMetadataErrors) =>
        inner.Replace(Landed(sourceFileName), Landed(destinationFileName), destinationBackupFileName is null ? null : Landed(destinationBackupFileName), ignoreMetadataErrors);

    public FileSystemStream Create(string path) => inner.Create(Landed(path));
    public FileSystemStream Create(string path, int bufferSize) => inner.Create(Landed(path), bufferSize);
    public FileSystemStream Create(string path, int bufferSize, FileOptions options) => inner.Create(Landed(path), bufferSize, options);
    public StreamWriter CreateText(string path) => inner.CreateText(Landed(path));
    public FileSystemStream Open(string path, FileMode mode) => inner.Open(Landed(path), mode);
    public FileSystemStream Open(string path, FileMode mode, FileAccess access) => inner.Open(Landed(path), mode, access);
    public FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share) => inner.Open(Landed(path), mode, access, share);
    public FileSystemStream Open(string path, FileStreamOptions options) => inner.Open(Landed(path), options);
    public FileSystemStream OpenRead(string path) => inner.OpenRead(Landed(path));
    public FileSystemStream OpenWrite(string path) => inner.OpenWrite(Landed(path));

    public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget) => inner.CreateSymbolicLink(Landed(path), pathToTarget);
    public IFileSystemInfo? ResolveLinkTarget(string linkPath, bool returnFinalTarget) => inner.ResolveLinkTarget(Landed(linkPath), returnFinalTarget);
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Decrypt(string path) => inner.Decrypt(Landed(path));
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Encrypt(string path) => inner.Encrypt(Landed(path));

    public FileAttributes GetAttributes(string path) => inner.GetAttributes(Landed(path));
    public FileAttributes GetAttributes(SafeFileHandle fileHandle) => inner.GetAttributes(fileHandle);
    public void SetAttributes(string path, FileAttributes fileAttributes) => inner.SetAttributes(Landed(path), fileAttributes);
    public void SetAttributes(SafeFileHandle fileHandle, FileAttributes fileAttributes) => inner.SetAttributes(fileHandle, fileAttributes);

    public DateTime GetCreationTime(string path) => inner.GetCreationTime(Landed(path));
    public DateTime GetCreationTime(SafeFileHandle fileHandle) => inner.GetCreationTime(fileHandle);
    public DateTime GetCreationTimeUtc(string path) => inner.GetCreationTimeUtc(Landed(path));
    public DateTime GetCreationTimeUtc(SafeFileHandle fileHandle) => inner.GetCreationTimeUtc(fileHandle);
    public DateTime GetLastAccessTime(string path) => inner.GetLastAccessTime(Landed(path));
    public DateTime GetLastAccessTime(SafeFileHandle fileHandle) => inner.GetLastAccessTime(fileHandle);
    public DateTime GetLastAccessTimeUtc(string path) => inner.GetLastAccessTimeUtc(Landed(path));
    public DateTime GetLastAccessTimeUtc(SafeFileHandle fileHandle) => inner.GetLastAccessTimeUtc(fileHandle);
    public DateTime GetLastWriteTime(string path) => inner.GetLastWriteTime(Landed(path));
    public DateTime GetLastWriteTime(SafeFileHandle fileHandle) => inner.GetLastWriteTime(fileHandle);
    public DateTime GetLastWriteTimeUtc(string path) => inner.GetLastWriteTimeUtc(Landed(path));
    public DateTime GetLastWriteTimeUtc(SafeFileHandle fileHandle) => inner.GetLastWriteTimeUtc(fileHandle);
    public void SetCreationTime(string path, DateTime creationTime) => inner.SetCreationTime(Landed(path), creationTime);
    public void SetCreationTime(SafeFileHandle fileHandle, DateTime creationTime) => inner.SetCreationTime(fileHandle, creationTime);
    public void SetCreationTimeUtc(string path, DateTime creationTimeUtc) => inner.SetCreationTimeUtc(Landed(path), creationTimeUtc);
    public void SetCreationTimeUtc(SafeFileHandle fileHandle, DateTime creationTimeUtc) => inner.SetCreationTimeUtc(fileHandle, creationTimeUtc);
    public void SetLastAccessTime(string path, DateTime lastAccessTime) => inner.SetLastAccessTime(Landed(path), lastAccessTime);
    public void SetLastAccessTime(SafeFileHandle fileHandle, DateTime lastAccessTime) => inner.SetLastAccessTime(fileHandle, lastAccessTime);
    public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc) => inner.SetLastAccessTimeUtc(Landed(path), lastAccessTimeUtc);
    public void SetLastAccessTimeUtc(SafeFileHandle fileHandle, DateTime lastAccessTimeUtc) => inner.SetLastAccessTimeUtc(fileHandle, lastAccessTimeUtc);
    public void SetLastWriteTime(string path, DateTime lastWriteTime) => inner.SetLastWriteTime(Landed(path), lastWriteTime);
    public void SetLastWriteTime(SafeFileHandle fileHandle, DateTime lastWriteTime) => inner.SetLastWriteTime(fileHandle, lastWriteTime);
    public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => inner.SetLastWriteTimeUtc(Landed(path), lastWriteTimeUtc);
    public void SetLastWriteTimeUtc(SafeFileHandle fileHandle, DateTime lastWriteTimeUtc) => inner.SetLastWriteTimeUtc(fileHandle, lastWriteTimeUtc);

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public UnixFileMode GetUnixFileMode(string path) => inner.GetUnixFileMode(Landed(path));
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public UnixFileMode GetUnixFileMode(SafeFileHandle fileHandle) => inner.GetUnixFileMode(fileHandle);
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void SetUnixFileMode(string path, UnixFileMode mode) => inner.SetUnixFileMode(Landed(path), mode);
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void SetUnixFileMode(SafeFileHandle fileHandle, UnixFileMode mode) => inner.SetUnixFileMode(fileHandle, mode);
}
