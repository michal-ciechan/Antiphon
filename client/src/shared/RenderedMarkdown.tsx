import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'

/**
 * The one markdown renderer (mobile-thread spec §4, M3): GFM markdown rendered exactly as the
 * agent-files review view always has. Extracted from `RenderedMarkdownReview` so the plan reader
 * reuses the same rendering instead of adding a third `react-markdown` call site; review marks,
 * section splitting and diffing stay with their owners — this component renders one markdown
 * string and nothing else.
 */
export function RenderedMarkdown({ children }: { children: string }) {
  return <Markdown remarkPlugins={[remarkGfm]}>{children}</Markdown>
}
