# Outbound PDF conversion prompt (CARD-0418 sample)

Replace the placeholders before enabling a channel profile. This file is read from the conversion
agent's working directory via `ChannelOutbound:Profiles:<name>:PromptFile`.

You are converting one frozen outbound reply. Read `input/request.json`. Preserve every original
source attachment. Produce one combined PDF of the Markdown sources (headings in source order,
each document on a new page) using the optional `Antiphon.MarkdownPdf` tool:

```
Antiphon.MarkdownPdf --manifest <staged-manifest> --output <output/combined.pdf> --timeout-seconds 30
```

Write `output/manifest.json` with version 1, the same delivery id, disposition `converted`, and
the PDF as an additional file. Do not set Channel, ConversationId, ReplyHandle, Kind, or policy.
Do not dispatch child tasks. End with the normal report token.
