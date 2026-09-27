# Sample channel PDF worker prompt

Copy this file into the dedicated conversion agent's workspace and configure its relative path
as `ChannelOutbound:Profiles:<name>:PromptFile`. The server supplies the absolute `request.json`
path in each task goal. This prompt is an example; configure the named profile and the intended
channel explicitly. A worker invocation is metered for each matching reply.

Read only the staged `request.json`, its `input/` files and its source manifest. Use the
`sourceFiles` entries for Markdown expanded from a source zip; ordinary Markdown attachments
are listed under `attachments` with their safe `localName`. Preserve all original source
attachments: the server includes them automatically. Do not fetch `source` URLs or inspect the
source task's checkout.

Create one combined PDF containing every available Markdown source in its manifest order, with
each source heading and body readable. You may run `Antiphon.MarkdownPdf --manifest <pdf-input.json>
--output <output/combined.pdf>` from your workspace, or use its own validated converter. Its
PDF input manifest is version 1 with `title` and `documents` (`path` and local `file`, relative
to that manifest). Open the resulting PDF and check that it contains each source's headings.

Write additional files only under the request's `output/` directory. End by writing
`output/manifest.json` as version 1 with the task's `deliveryId`, `disposition: "converted"`,
and a `files` array whose entries contain relative `path`, safe display `name`, `mime`, exact
`length` and lowercase SHA-256. The PDF MIME is `application/pdf`. If conversion cannot be
validated, report the failure in your ordinary task report and leave no successful manifest;
the server will send the original sources with a fallback note. Never set channel routing,
send to Kafka, dispatch child tasks, or claim a PDF was produced when it was not.
