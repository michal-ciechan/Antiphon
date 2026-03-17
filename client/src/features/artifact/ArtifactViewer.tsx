import { lazy, Suspense, useState, useCallback, useMemo } from 'react'
import { Box, Loader, Group } from '@mantine/core'
import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import rehypeHighlight from 'rehype-highlight'
import rehypeRaw from 'rehype-raw'
import type { Components } from 'react-markdown'
import { ArtifactContextHint } from './ArtifactContextHint'
import { VersionHistory, type ArtifactVersion } from './VersionHistory'
import 'highlight.js/styles/github-dark.css'

// Lazy-load mermaid component — only fetched when a mermaid code block is present (UX-DR17)
const MermaidDiagram = lazy(() => import('./MermaidDiagram'))

interface ArtifactViewerProps {
  /** Raw markdown content */
  content: string
  /** Number of revisions */
  revisions?: number
  /** ISO timestamp of artifact creation */
  createdAt?: string
  /** Callback to view the conversation that produced this artifact */
  onViewConversation?: () => void
  /** Whether to use constrained width (~900px) or full-width */
  constrainedWidth?: boolean
  /** Version history entries */
  versions?: ArtifactVersion[]
  /** Current version being viewed */
  currentVersion?: number
  /** Callback when a version is selected from history */
  onSelectVersion?: (version: number) => void
}

/**
 * Main artifact renderer (UX-DR17, Story 2.11).
 * Renders markdown via react-markdown with GFM tables, task lists, strikethrough,
 * syntax-highlighted code blocks, and lazy-loaded Mermaid diagrams.
 */
export function ArtifactViewer({
  content,
  revisions = 1,
  createdAt,
  onViewConversation,
  constrainedWidth = true,
  versions,
  currentVersion = 1,
  onSelectVersion,
}: ArtifactViewerProps) {
  const [hasMermaid] = useState(() => /```mermaid/i.test(content))

  const handleSelectVersion = useCallback(
    (v: number) => onSelectVersion?.(v),
    [onSelectVersion],
  )

  // Custom components to intercept mermaid code fences
  const components: Components = useMemo(
    () => ({
      code({ className, children, ...props }) {
        const match = /language-(\w+)/.exec(className || '')
        const language = match?.[1]

        if (language === 'mermaid') {
          const chart = String(children).replace(/\n$/, '')
          return (
            <Suspense
              fallback={
                <Box
                  style={{
                    display: 'flex',
                    justifyContent: 'center',
                    padding: 'var(--mantine-spacing-md)',
                  }}
                >
                  <Loader size="sm" />
                </Box>
              }
            >
              <MermaidDiagram chart={chart} />
            </Suspense>
          )
        }

        // For inline code (no language class), render as inline
        if (!className) {
          return (
            <code className={className} {...props}>
              {children}
            </code>
          )
        }

        // For fenced code blocks with syntax highlighting
        return (
          <code className={className} {...props}>
            {children}
          </code>
        )
      },
      // Render pre blocks with proper styling
      pre({ children, ...props }) {
        return (
          <pre
            style={{
              backgroundColor: 'var(--mantine-color-dark-7)',
              padding: 'var(--mantine-spacing-sm)',
              borderRadius: 'var(--mantine-radius-sm)',
              overflow: 'auto',
              fontSize: '0.875rem',
              lineHeight: 1.6,
            }}
            {...props}
          >
            {children}
          </pre>
        )
      },
      // GFM table styling
      table({ children, ...props }) {
        return (
          <Box style={{ overflowX: 'auto', marginBottom: 'var(--mantine-spacing-md)' }}>
            <table
              style={{
                width: '100%',
                borderCollapse: 'collapse',
                fontSize: '0.875rem',
              }}
              {...props}
            >
              {children}
            </table>
          </Box>
        )
      },
      th({ children, ...props }) {
        return (
          <th
            style={{
              borderBottom: '2px solid var(--mantine-color-dark-4)',
              padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
              textAlign: 'left',
              fontWeight: 600,
            }}
            {...props}
          >
            {children}
          </th>
        )
      },
      td({ children, ...props }) {
        return (
          <td
            style={{
              borderBottom: '1px solid var(--mantine-color-dark-5)',
              padding: 'var(--mantine-spacing-xs) var(--mantine-spacing-sm)',
            }}
            {...props}
          >
            {children}
          </td>
        )
      },
    }),
    // Only re-create components if hasMermaid changes (it won't, but just in case)
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [hasMermaid],
  )

  return (
    <Box
      style={{
        maxWidth: constrainedWidth ? 900 : undefined,
        margin: constrainedWidth ? '0 auto' : undefined,
        width: '100%',
        padding: 'var(--mantine-spacing-md)',
      }}
    >
      {/* Context hint + version history header */}
      <Group justify="space-between" align="flex-start" wrap="nowrap">
        {createdAt && (
          <ArtifactContextHint
            revisions={revisions}
            createdAt={createdAt}
            onViewConversation={onViewConversation}
          />
        )}
        {versions && versions.length > 1 && onSelectVersion && (
          <VersionHistory
            versions={versions}
            currentVersion={currentVersion}
            onSelectVersion={handleSelectVersion}
          />
        )}
      </Group>

      {/* Markdown content */}
      <Box
        className="artifact-markdown"
        style={{
          lineHeight: 1.7,
          fontSize: '0.95rem',
          color: 'var(--mantine-color-text)',
        }}
      >
        <Markdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[rehypeHighlight, rehypeRaw]}
          components={components}
        >
          {content}
        </Markdown>
      </Box>
    </Box>
  )
}
