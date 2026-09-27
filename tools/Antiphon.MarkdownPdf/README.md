# Optional Markdown to PDF command

`Antiphon.MarkdownPdf` is a standalone command for a channel conversion worker. The server does not load or invoke it during task settlement. A worker may call it with staged Markdown files and return the resulting PDF through the generic outbound output manifest once that channel workflow is configured.

```sh
dotnet Antiphon.MarkdownPdf.dll --manifest request.json --output output/combined.pdf --browser-path /path/to/chromium --timeout-seconds 30
```

The browser path is optional on Windows, where the command searches Edge and Chrome defaults. The output directory must already exist. The timeout accepts 1–300 seconds. A failed render exits nonzero and cannot claim a stale PDF as this invocation's output.

Input manifest version 1:

```json
{
  "version": 1,
  "title": "Project deliverables",
  "documents": [
    { "path": "docs/features/example/requirements.md", "file": "input/requirements.md" },
    { "path": "docs/features/example/design.md", "file": "input/design.md" }
  ]
}
```

`file` is relative to the manifest directory. The command reads no server configuration and has no database, broker or gateway dependency. It limits staged input to 64 MiB and renders the ordered documents with path headings and page breaks.
