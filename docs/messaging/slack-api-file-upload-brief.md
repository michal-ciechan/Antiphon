# Slack file upload — HTML snippet behaviour

Outbound files go through Slack's external-upload trio (`files.getUploadURLExternal` → PUT the
bytes → `files.completeUploadExternal`). `files.upload` is deprecated and closed to new apps.

**HTML is rendered as a text snippet, not a document.** Slack shows an attached `.html` / `.htm`
file as inline snippet text regardless of the MIME we send (`text/html` after CARD-0250, or the
previous `application/octet-stream` fallback — same rendering). This is Slack's own preview, not
a bug in Antiphon's upload path, and it is why channel-bound instructions say to prefer PDF for
documents. Antiphon does **not** refuse HTML attachments: a user who asked for the `.html` file
should still receive it.

See `src/Antiphon.Messaging.Slack/SlackChannelAdapter.cs` (`SendAttachmentAsync`) and
CARD-0250 (`InferMime` `.html`/`.htm` → `text/html`).
