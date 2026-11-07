# Antiphon.IO.WriteBehind

A write-behind decorator for [System.IO.Abstractions](https://github.com/TestableIO/System.IO.Abstractions).
Whole-file writes return immediately and land on disk on a background thread. Reads through the same
`IFileSystem` see them straight away.

```csharp
using var fs = new WriteBehindFileSystem(new FileSystem());

fs.File.WriteAllText(path, json);   // returns at once
fs.File.ReadAllText(path);          // the new json, from memory
fs.Flush();                         // everything on disk; throws writes that failed
```

## Behaviour

- **Queued:** `File.WriteAllText`, `WriteAllBytes` and `WriteAllLines`, including the async and span
  overloads.
  - The bytes match System.IO, including byte-order marks.
  - A missing directory, or a path that is a directory, is refused at the call.
- **Atomic landing:** each write is a temporary file renamed over the target. Another process sees the old
  file or the new one, never a partial one. Turn this off with `AtomicReplace = false`.
- **Coalesced:** a path written again before it landed keeps only the newest content. Files land in the
  order they were last written.
- **Answered from memory:** `File.ReadAll*`, `ReadLines`, `OpenText` and `Exists`.
- **`File.Delete`:** drops the queued write.
- **Waits for that path's write first:** every other `File` operation on a path, plus `FileInfo`,
  `FileStream` and `FileVersionInfo` creation.
- **Waits for all queued writes first:** directory listings, moves, deletes and dates, and `DirectoryInfo`
  creation.
- **Not intercepted:** `Path`, `DriveInfo` and `FileSystemWatcher` go straight to the inner file system.
  Streams and info objects read the disk directly once created.
- **Failures:** a write that fails in the background is raised by the next `Flush()` or `Dispose()` as an
  `AggregateException` of `WriteBehindException`, and its content is dropped. `OnWriteFailed` reports it as
  it happens.
- **Shutdown:** the writer runs on a foreground thread by default (`KeepProcessAlive`), so a normal exit
  waits for the queue. A crash or a killed process loses queued writes.
